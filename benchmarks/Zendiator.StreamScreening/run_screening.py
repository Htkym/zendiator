"""Fixed Linux-only controller. Implementation only; do not run before BOCCHI approval."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

SHAPES = ('completed', 'suspended', 'open-session', 'large-middleware-session', 'zero-hook-sentinel')
ENV = {'DOTNET_TieredCompilation': '1', 'DOTNET_TieredPGO': '1', 'DOTNET_ReadyToRun': '0',
       'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER': '1'}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--execute-approved-screening', action='store_true')
    parser.add_argument('--dotnet', required=True, type=Path)
    parser.add_argument('--repo', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    if not args.execute_approved_screening:
        raise SystemExit('Measurement is disabled; a separately approved explicit invocation is required.')
    if sys.platform != 'linux' or not args.dotnet.is_absolute() or not args.repo.is_absolute() or not args.output.is_absolute():
        raise SystemExit('Use the approved private CPU Linux VM and absolute paths only.')
    args.output.mkdir(exist_ok=False, parents=True)
    start = time.monotonic()
    deadline = start + 420
    env = os.environ.copy()
    env.update(ENV)
    commands, children = [], []
    boot_path = Path('/proc/sys/kernel/random/boot_id')
    boot_id = boot_path.read_text().strip()

    def save(name, value):
        (args.output / name).write_text(json.dumps(value, indent=2), encoding='utf-8')

    def command(label, argv, cap):
        if boot_path.read_text().strip() != boot_id:
            raise RuntimeError('VM identity changed;stop without combining results.')
        remaining = deadline - time.monotonic()
        if remaining <= 2:
            raise TimeoutError('Remote 420 second cap reached.')
        command_deadline = time.monotonic() + min(cap, remaining)
        record = {'label': label, 'argv': list(map(str, argv)), 'env': ENV,
                  'capSeconds': min(cap, remaining), 'startedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat()}
        commands.append(record)
        save('commands.json', commands)
        with (args.output / (label + '.stdout.txt')).open('wb') as stdout, (args.output / (label + '.stderr.txt')).open('wb') as stderr:
            process = subprocess.Popen(argv, cwd=args.repo, env=env, stdout=stdout, stderr=stderr, start_new_session=True)
            children.append(process.pid)
            record['pid'] = process.pid
            try:
                # Two seconds of this same command budget are reserved for
                # owned-group termination, rather than extending the cap.
                code = process.wait(timeout=max(.001, command_deadline - time.monotonic() - 2))
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=1)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait(timeout=1)
                record['timedOut'] = True
                record['exitCode'] = process.returncode
                save('commands.json', commands)
                raise TimeoutError(label + ': fixed budget exceeded; no retry.')
            record['exitCode'] = code
            save('commands.json', commands)
            # Check only this command's process group. Never inspect/kill other Work processes.
            try:
                os.killpg(process.pid, 0)
            except ProcessLookupError:
                record['ownedGroupEmpty'] = True
            else:
                os.killpg(process.pid, signal.SIGKILL)
                record['ownedGroupEmpty'] = False
                save('commands.json', commands)
                raise RuntimeError('Owned process group did not end.')
            save('commands.json', commands)
            if code != 0:
                raise RuntimeError(label + ': failed; no correction or retry.')

    status = {'phase': 'screening', 'startedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'bootId': boot_id, 'remoteSecondsCap': 420, 'recoverySecondsReserved': 120, 'overallSecondsCap': 600,
              'automaticRemeasurement': False, 'publicAdoption': False}
    save('lifecycle.json', status)
    try:
        command('sdk-version', [args.dotnet, '--version'], 15)
        if (args.output / 'sdk-version.stdout.txt').read_text().strip() != '10.0.401':
            raise RuntimeError('SDK pin mismatch.')
        save('environment.json', {'bootId': boot_id, 'cpuinfo': Path('/proc/cpuinfo').read_text(), 'jitEnv': ENV,
                                  'platform': os.uname()._asdict() if hasattr(os.uname(), '_asdict') else list(os.uname())})
        project = args.repo / 'benchmarks/Zendiator.StreamScreening/Zendiator.StreamScreening.csproj'
        command('build', [args.dotnet, 'build', project, '-c', 'Release', '--disable-build-servers',
                          '-p:UseSharedCompilation=false', '-p:NuGetAudit=false'], 120)
        harness = project.parent / 'bin/Release/net10.0/Zendiator.StreamScreening.dll'
        consumers = args.output / 'consumers'
        command('prepare-consumers', [args.dotnet, harness, 'prepare', consumers], 60)
        measurement_start = time.monotonic()
        for shape in SHAPES:
            for launch in range(1, 4):
                if time.monotonic() - measurement_start >= 180:
                    raise TimeoutError('15-process aggregate 180 second cap reached.')
                raw = args.output / (shape + '-' + str(launch) + '.json')
                command(shape + '-' + str(launch), [args.dotnet, harness, 'run', consumers / shape / 'consumer.dll',
                                                  shape, str(launch), raw], min(12, 180 - (time.monotonic() - measurement_start)))
        status['status'] = 'completed-fixed-budget'
    except Exception as error:
        status['status'] = 'stopped-without-retry'
        status['reason'] = str(error)
    finally:
        status['remoteSeconds'] = time.monotonic() - start
        status['ownedChildren'] = children
        status['allOwnedGroupsEmpty'] = all(item.get('ownedGroupEmpty', False) for item in commands if 'pid' in item)
        status['vmStopRequiredByOuterOwner'] = True
        save('lifecycle.json', status)
        pins = [{'path': str(path.relative_to(args.output)), 'bytes': path.stat().st_size,
                 'sha256': hashlib.sha256(path.read_bytes()).hexdigest().upper()}
                for path in sorted(args.output.rglob('*')) if path.is_file() and path.name != 'result-pins.json']
        save('result-pins.json', pins)
    return 0 if status['status'] == 'completed-fixed-budget' else 1


if __name__ == '__main__':
    sys.exit(main())
