"""Read-only audit of the fixed 1c41d2b baseline before 0.5.0 experiments."""

import argparse
import csv
import hashlib
import json
import re
import sys
import zipfile
from collections import Counter
from pathlib import Path, PurePosixPath

from artifact_gate import read, sha
from interleaved_plan import identity, normalize_case


SOURCE_SHA = "1c41d2b105073d2dc9be2c0e8684fdce2fa11f55"
COUNTS = {"send": 125, "features": 55, "streams": 120, "relay": 60}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def within(root, name):
    path = (root / name).resolve()
    require(path.is_relative_to(root.resolve()), f"Path outside baseline root: {name}")
    return path


def verify_archive(archive, inventory):
    names = [entry["path"] for entry in inventory]
    actual = archive.namelist()
    require(len(set(names)) == len(names), "Duplicate archive manifest path")
    require(len(set(actual)) == len(actual), "Duplicate ZIP member")
    expected = set(names)
    # The preserved ZIP includes its inventory, whose own hash is pinned outside the ZIP.
    if "raw-archive-manifest.json" in actual and "raw-archive-manifest.json" not in expected:
        require(archived_json(archive, "raw-archive-manifest.json") == inventory, "Embedded inventory mismatch")
        expected.add("raw-archive-manifest.json")
    require(set(actual) == expected,
            f"Missing or unexpected raw ZIP member: missing={sorted(expected - set(actual))}, "
            f"unexpected={sorted(set(actual) - expected)}")
    for entry in inventory:
        name = entry["path"]
        parts = PurePosixPath(name)
        require(not parts.is_absolute() and ".." not in parts.parts and "\\" not in name,
                f"Invalid archive path: {name}")
        payload = archive.read(name)
        require(len(payload) == entry["bytes"] and
                hashlib.sha256(payload).hexdigest().upper() == entry["sha256"].upper(),
                f"Raw archive hash/size mismatch: {name}")


def archived_json(archive, name):
    return json.loads(archive.read(name).decode("utf-8-sig"))


def report_case(benchmark):
    case = {"Type": benchmark["Type"], "Method": benchmark["Method"]}
    for item in re.split(r"[,\&]\s*", benchmark.get("Parameters") or ""):
        if not item:
            continue
        name, value = item.split("=", 1)
        require(name in ("Lifetime", "Count", "Asynchronous") and name not in case,
                f"Unexpected benchmark parameter: {item}")
        require(name != "Asynchronous" or value in ("True", "False"), f"Invalid boolean parameter: {item}")
        case[name] = (int(value) if name == "Count" else
                      value == "True" if name == "Asynchronous" else value.strip('"'))
    return case


def verify_case(benchmark, record, expected):
    label = record["caseId"]
    require(identity(report_case(benchmark)) == identity(normalize_case(expected["Case"])),
            f"Workload mismatch: {label}")
    require(identity({"Type": record["type"], "Method": record["method"],
                      "Lifetime": record["lifetime"], "Count": record["count"],
                      "Asynchronous": record["asynchronous"]}) == identity(report_case(benchmark)) and
            record["library"] == expected["Library"] and
            record["comparisonKey"] == expected["ComparisonKey"], f"Record identity mismatch: {label}")
    stats = benchmark["Statistics"]
    require(stats is not None, f"Missing statistics: {label}")
    measurements = benchmark["Measurements"]
    stages = {stage: [row for row in measurements if row["IterationMode"] == "Workload" and
                     row["IterationStage"] == stage] for stage in ("Actual", "Result", "Warmup")}
    actual, result = stages["Actual"], stages["Result"]
    actual_ids = [(row["LaunchIndex"], row["IterationIndex"]) for row in actual]
    result_ids = [(row["LaunchIndex"], row["IterationIndex"]) for row in result]
    require(actual_ids == [(1, iteration) for iteration in range(1, 13)] and len(stages["Warmup"]) == 20 and
            len(set(result_ids)) == len(result) and set(result_ids).issubset(actual_ids),
            f"Invalid measurement identities: {label}")
    require(len(result) == stats["N"] == record["validN"] and record["rawActualN"] == 12 and
            [row["Nanoseconds"] / row["Operations"] for row in result] ==
            stats["OriginalValues"] == record["statisticsOriginalValues"],
            f"Statistics/raw mismatch: {label}")
    require(all(record[field] == stats[metric] for field, metric in
                (("meanNs", "Mean"), ("medianNs", "Median"), ("stdDevNs", "StandardDeviation"))) and
            record["allocatedBytes"] == benchmark["Memory"]["BytesAllocatedPerOperation"],
            f"Summary/raw mismatch: {label}")
    excluded = [(label, row["LaunchIndex"], row["IterationIndex"], row["Operations"], row["Nanoseconds"])
                for row in actual if (row["LaunchIndex"], row["IterationIndex"]) not in result_ids]
    require(record["excludedIterations"] == len(excluded), f"Excluded count mismatch: {label}")
    return excluded


def validate(root, manifest):
    require(manifest["schemaVersion"] == 1 and manifest["sourceSha"] == SOURCE_SHA,
            "Wrong baseline SHA or schema")
    results = within(root, manifest["results"])
    control = within(root, manifest["control"])
    pins = manifest["pins"]
    required_pins = {".local/CURRENT-BASELINE.json", manifest["results"] + "/session.json"}
    required_pins.update(manifest["results"] + "/review-materials/" + name for name in
                         ("verification.json", "verified-results.json", "raw-archive-manifest.json",
                          "raw-bdn-evidence.zip", "excluded-actual-iterations.csv", "../all-results.csv"))
    required_paths = {within(root, name) for name in required_pins}
    require(required_paths.issubset({within(root, pin["path"]) for pin in pins}), "Missing required baseline pin")
    require(len({pin["path"] for pin in pins}) == len(pins), "Duplicate pinned file")
    for pin in pins:
        path = within(root, pin["path"])
        require(path.stat().st_size == pin["bytes"] and sha(path) == pin["sha256"].upper(),
                f"Pinned file hash/size mismatch: {pin['path']}")
    review = results / "review-materials"
    current = read(root / ".local/CURRENT-BASELINE.json")
    session = read(results / "session.json")
    verified = read(review / "verified-results.json")
    verification = read(review / "verification.json")
    require(current["sourceSha"] == session["revision"] == verification["fixedSha"] == SOURCE_SHA,
            "Wrong baseline source SHA")
    require(Path(current["results"]).resolve() == results and
            Path(current["control"]).resolve() == control, "Baseline pointer mismatch")
    require(session["sessionId"] == verification["sessionId"] == manifest["sessionId"] and
            session["sourceDigest"] == verification["sourceDigest"] == manifest["sourceDigest"],
            "Mixed baseline session/source digest")
    require(session["sdk"] == "10.0.401" and session["runtime"] == ".NET 10.0.12",
            "Wrong baseline runtime/SDK")
    require(verified["verification"] == verification and verification["counts"] == COUNTS and
            verification["rawActualN"] == 4320 and verification["validN"] == 4042 and
            verification["totalCases"] == current["allCases"] == 360 and
            current["rawActualN"] == 4320 and current["validN"] == 4042 and
            verification["historicalResultsMixed"] is False and
            verification["cleanupVerified"] is True, "Baseline verification mismatch")
    blocks = verification["blocks"]
    require(all(0 < block["preparationThroughCleanupSeconds"] <= 3300 and block["cleanupVerified"]
                for block in blocks) and sum(block["completedCases"] for block in blocks) == 360 and
            abs(sum(block["preparationThroughCleanupSeconds"] for block in blocks) - 17255.54761) < .001,
            "Baseline duration/cleanup mismatch")
    records = verified["records"]
    index = {record["caseId"]: record for record in records}
    require(len(index) == len(records) == 360 and
            Counter(record["group"] for record in records) == COUNTS, "Missing/duplicate baseline cases")
    with (results / "all-results.csv").open(encoding="utf-8-sig", newline="") as stream:
        summaries = list(csv.DictReader(stream))
    by_workload = {(record["group"], record["type"], record["method"],
                    str(record["lifetime"] or ""), str(record["count"]) if record["count"] is not None else "",
                    str(record["asynchronous"]) if record["asynchronous"] is not None else ""): record
                   for record in records}
    require(len(by_workload) == len(summaries) == 360, "Duplicate/missing summary workload")
    seen = set()
    for row in summaries:
        key = tuple(row[field] for field in ("Group", "Type", "Method", "Lifetime", "Count", "Asynchronous"))
        require(key in by_workload and key not in seen, f"Unexpected/duplicate summary workload: {key}")
        seen.add(key)
        record = by_workload[key]
        require(all(float(row[field]) == record[metric] for field, metric in
                    (("MeanNs", "meanNs"), ("MedianNs", "medianNs"), ("StdDevNs", "stdDevNs"),
                     ("AllocatedBytes", "allocatedBytes"), ("N", "validN"))), f"CSV/record mismatch: {key}")
    inventory = read(review / "raw-archive-manifest.json")
    excluded = []
    retained = {}
    with zipfile.ZipFile(review / "raw-bdn-evidence.zip") as archive:
        verify_archive(archive, inventory)
        require(archived_json(archive, "session.json") == session, "Archive/session mismatch")
        for group, count in COUNTS.items():
            plan = archived_json(archive, f"control/planned-{group}.json")
            require(plan == read(control / f"planned-{group}.json") and len(plan) == count and
                    len({identity(row["Case"]) for row in plan}) == count, f"Invalid plan: {group}")
            gate = archived_json(archive, f"runs/{group}/correctness.json")
            require(len(gate) == 394 and all(row["status"] == "Passed" for row in gate),
                    f"Invalid correctness gate: {group}")
            info = archived_json(archive, f"runs/{group}/run-info.json")
            require(info["revision"] == SOURCE_SHA and info["sourceDigest"] == session["sourceDigest"] and
                    info["expectedCases"] == info["childCount"] == count and
                    all(info[field] == session[field] for field in ("sdk", "runtime", "profile")),
                    f"Mixed run identity: {group}")
            for ordinal, expected in enumerate(plan, 1):
                record = index[f"{group}-{ordinal:04d}"]
                require(record["group"] == group, f"Wrong case group: {record['caseId']}")
                prefix = f"runs/{group}/cases/{ordinal:04d}/attempt-0001/"
                report = Path(record["reportPath"]).relative_to(results).as_posix()
                require(report.startswith(prefix + "results/"), f"Wrong report path: {report}")
                payload = archive.read(report)
                require(hashlib.sha256(payload).hexdigest().upper() == record["reportSha256"],
                        f"Report hash mismatch: {record['caseId']}")
                document = json.loads(payload)
                require(len(document["Benchmarks"]) == 1, f"Invalid report count: {report}")
                excluded.extend(verify_case(document["Benchmarks"][0], record, expected))
                binding = archived_json(archive, prefix + "artifact-binding.json")
                proof = archived_json(archive, prefix + "artifact-gate/artifact-proof.json")
                require(binding["session"] == proof["session"] == session["sessionId"] and
                        binding["semantic"]["revision"] == SOURCE_SHA and
                        proof["benchmarkGateCases"] == 394 and
                        binding["bundleBeforeAndAfterVerified"] and proof["bundleBeforeAndAfterVerified"],
                        f"Invalid artifact identity: {record['caseId']}")
                bundle = within(results, prefix + "bundle")
                for name, expected_hash in (("Zendiator.dll", record["childRuntimeSha256"]),
                                            ("Zendiator.UseCaseBenchmarks.dll", record["consumerSha256"])):
                    relative = "bin/Release/net10.0/" + name
                    path = within(bundle, relative)
                    require(binding["semantic"]["bundleFiles"][relative] == expected_hash and
                            sha(path) == expected_hash, f"Retained binary hash mismatch: {path}")
                    retained[path.relative_to(root).as_posix()] = expected_hash
    with (review / "excluded-actual-iterations.csv").open(encoding="utf-8-sig", newline="") as stream:
        rows = list(csv.DictReader(stream))
    declared = [(row["CaseId"], int(row["launchIndex"]), int(row["iterationIndex"]),
                 int(row["operations"]), float(row["rawNanosecondsBeforeOverheadCorrection"])) for row in rows]
    require(len(excluded) == 278 and Counter(excluded) == Counter(declared), "Excluded raw identities mismatch")
    require(sum(record["validN"] for record in records) == 4042, "Wrong valid iteration total")
    return {"status": "verified", "sourceSha": SOURCE_SHA, "counts": COUNTS, "cases": 360,
            "rawActualN": 4320, "validN": 4042, "excludedIterations": 278, "gateChecksPerGroup": 394,
            "durationSeconds": 17255.54761, "archiveMembers": len(archive.namelist()),
            "rawPayloadMembers": len(inventory), "pinnedFiles": len(pins),
            "retainedFiles": retained,
            "retainedBinaryScope": "runtime and consumer per case; other bundle files use the preserved historical audit",
            "newMeasurements": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--repository-root", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    try:
        result = validate(args.repository_root.resolve(), read(args.manifest))
    except (ValueError, KeyError, OSError, zipfile.BadZipFile) as error:
        print(f"Baseline audit failed: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
