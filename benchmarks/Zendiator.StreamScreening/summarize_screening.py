"""Summarize fixed raw files only; never launches measurement or retries."""
import json
from pathlib import Path
import statistics
import sys

SHAPES = ('completed', 'suspended', 'open-session', 'large-wrapper-session', 'zero-hook-sentinel')


def summarize(root):
    boot_ids, harness_pins = set(), set()
    result = []
    for shape in SHAPES:
        launches = []
        for launch in range(1, 4):
            data = json.loads((root / f'{shape}-{launch}.json').read_text())
            if not data['providerAndWorkerDisposalCompleted'] or data['exitCode'] != 0:
                raise ValueError('Incomplete ownership/exit proof.')
            boot_ids.add(data['environment']['vmBootId'])
            harness_pins.add(data['harnessSha256'])
            rows = data['consumer']['rows']
            if len(rows) != 24:
                raise ValueError('Fixed raw batch count differs.')
            timing, allocation, quality = {}, {}, []
            for backend in ('A', 'B'):
                times = [row['NsPerEnumeration'] for row in rows if row['Backend'] == backend and row['Pass'] == 'timing']
                allocs = [row['BytesPerEnumeration'] for row in rows if row['Backend'] == backend and row['Pass'] == 'allocation']
                if len(times) != 8 or len(allocs) != 4 or min(times) <= 0 or min(allocs) < 0:
                    raise ValueError('Invalid raw count/value.')
                threshold = 1.35 if shape == 'suspended' else 1.20
                if max(times) / min(times) > threshold or min(times) * 512 < 1_000_000:
                    quality.append(backend + ':timing-variance-or-short-block')
                if max(allocs) - min(allocs) > max(16, statistics.median(allocs) * .10):
                    quality.append(backend + ':allocation-variance')
                timing[backend] = statistics.median(times)
                allocation[backend] = statistics.median(allocs)
            timing_ratios = []
            allocation_differences = []
            for batch in range(8):
                pair = {row['Backend']: row for row in rows if row['Pass'] == 'timing' and row['Batch'] == batch}
                timing_ratios.append(pair['B']['NsPerEnumeration'] / pair['A']['NsPerEnumeration'])
            for batch in range(4):
                pair = {row['Backend']: row for row in rows if row['Pass'] == 'allocation' and row['Batch'] == batch}
                allocation_differences.append(pair['B']['BytesPerEnumeration'] - pair['A']['BytesPerEnumeration'])
            launches.append({'launch': launch, 'consumerSha256': data['consumerSha256'], 'timing': timing,
                             'allocation': allocation, 'pairedTimingRatios': timing_ratios,
                             'pairedAllocationDifferences': allocation_differences,
                             'allocationRatio': allocation['B'] / allocation['A'] if allocation['A'] else None,
                             'timingRange': {backend: [min(row['NsPerEnumeration'] for row in rows if row['Backend'] == backend and row['Pass'] == 'timing'), max(row['NsPerEnumeration'] for row in rows if row['Backend'] == backend and row['Pass'] == 'timing')] for backend in ('A', 'B')},
                             'allocationRange': {backend: [min(row['BytesPerEnumeration'] for row in rows if row['Backend'] == backend and row['Pass'] == 'allocation'), max(row['BytesPerEnumeration'] for row in rows if row['Backend'] == backend and row['Pass'] == 'allocation')] for backend in ('A', 'B')},
                             'timingRatio': statistics.median(timing_ratios),
                             'allocationDifference': statistics.median(allocation_differences),
                             'quality': quality})
        if len({item['consumerSha256'] for item in launches}) != 1:
            raise ValueError('Different consumer DLLs combined.')
        threshold = 1.35 if shape == 'suspended' else 1.20
        variance = any(item['quality'] for item in launches)
        for backend in ('A', 'B'):
            medians = [item['timing'][backend] for item in launches]
            variance |= max(medians) / min(medians) > threshold
        extra = [item['allocationDifference'] > 0 for item in launches]
        if shape == 'zero-hook-sentinel':
            decision = 'observation-only:dispatch-overhead-and-stability;not-a-before-after-regression-gate'
        elif all(extra):
            decision = 'hold:reproduced-additional-process-managed-allocation-even-below-16B-or-5percent'
        elif any(extra):
            decision = 'hold:allocation-direction-unresolved;no-noninferiority-proof'
        elif variance:
            decision = 'hold:variance-or-insufficient-fixed-sample'
        elif all(item['timingRatio'] > 1.05 for item in launches):
            decision = 'no-adoption-evidence:timing-regression'
        else:
            speed = all(item['timingRatio'] <= .95 for item in launches)
            allocation_gain = all(-item['allocationDifference'] >= max(16, item['allocation']['A'] * .05) for item in launches)
            no_large_time_regression = all(item['timingRatio'] <= 1.05 for item in launches)
            decision = 'next-finite-review-candidate:not-public-adoption-or-noninferiority-proof' if (speed or allocation_gain) and no_large_time_regression else 'hold:insufficient-or-mixed-evidence'
        result.append({'shape': shape, 'launches': launches, 'decision': decision,
                       'medianOfProcessMedians': {metric: {backend: statistics.median(item[metric][backend] for item in launches) for backend in ('A', 'B')} for metric in ('timing', 'allocation')},
                       'sentinelAffectsOtherShapes': False,
                       'allocationScope': 'whole process managed allocation including worker;not generated-only;native excluded'})
    if len(boot_ids) != 1 or len(harness_pins) != 1:
        raise ValueError('Different VM/harness combined.')
    return {'shapes': result, 'bootId': next(iter(boot_ids)), 'harnessSha256': next(iter(harness_pins)),
            'additionalMeasurementRequested': False, 'publicAdoption': False}


if __name__ == '__main__':
    directory = Path(sys.argv[1])
    summary = summarize(directory)
    with (directory / 'screening-summary.json').open('x', encoding='utf-8') as output:
        json.dump(summary, output, indent=2)
