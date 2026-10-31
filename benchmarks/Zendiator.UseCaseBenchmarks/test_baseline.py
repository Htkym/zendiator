"""Synthetic fixed-baseline artifacts; no dotnet, benchmark, or external process."""

import copy
import csv
import hashlib
import json
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path

import verify_baseline as audit


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value), encoding="utf-8")


def fixture(root):
    results = root / ".local/results"
    review = results / "review-materials"
    control = root / ".local/control"
    session = {"revision": audit.SOURCE_SHA, "sessionId": "synthetic", "sourceDigest": "synthetic",
               "sdk": "10.0.401", "runtime": ".NET 10.0.12", "profile": "synthetic"}
    verification = {"fixedSha": audit.SOURCE_SHA, "sessionId": "synthetic", "sourceDigest": "synthetic",
                    "counts": audit.COUNTS, "rawActualN": 4320, "validN": 4042, "totalCases": 360,
                    "historicalResultsMixed": False, "cleanupVerified": True,
                    "blocks": [{"completedCases": 72, "preparationThroughCleanupSeconds": 17255.54761 / 6,
                                "cleanupVerified": True} for _ in range(5)] +
                              [{"completedCases": 0, "preparationThroughCleanupSeconds": 17255.54761 / 6,
                                "cleanupVerified": True}]}
    save(results / "session.json", session)
    save(root / ".local/CURRENT-BASELINE.json", {"sourceSha": audit.SOURCE_SHA, "results": str(results),
         "control": str(control), "allCases": 360, "rawActualN": 4320, "validN": 4042})
    save(review / "verification.json", verification)
    archive_data = {"session.json": session}
    records, excluded, summaries = [], [], []
    binary = b"synthetic preserved binary"
    binary_hash = hashlib.sha256(binary).hexdigest().upper()
    for group, count in audit.COUNTS.items():
        plan = [{"Case": {"Type": f"Zendiator{group}{ordinal}", "Method": "Typed", "Lifetime": "Scoped"},
                 "Library": "Zendiator", "ComparisonKey": [group, ordinal]} for ordinal in range(1, count + 1)]
        save(control / f"planned-{group}.json", plan)
        archive_data[f"control/planned-{group}.json"] = plan
        archive_data[f"runs/{group}/correctness.json"] = [{"status": "Passed"}] * 394
        archive_data[f"runs/{group}/run-info.json"] = {**session, "expectedCases": count, "childCount": count}
        for ordinal, expected in enumerate(plan, 1):
            label = f"{group}-{ordinal:04d}"
            prefix = f"runs/{group}/cases/{ordinal:04d}/attempt-0001/"
            report = prefix + "results/report-full.json"
            n = 11 if len(records) < 278 else 12
            rows = [{"IterationMode": "Workload", "IterationStage": stage, "LaunchIndex": 1,
                     "IterationIndex": iteration, "Nanoseconds": float(iteration), "Operations": 1}
                    for stage, length in (("Warmup", 20), ("Actual", 12), ("Result", n))
                    for iteration in range(1, length + 1)]
            stats = {"N": n, "OriginalValues": list(range(1, n + 1)), "Mean": (n + 1) / 2,
                     "Median": (n + 1) / 2, "StandardDeviation": 1.0}
            benchmark = {"Type": expected["Case"]["Type"], "Method": "Typed", "Parameters": "Lifetime=Scoped",
                         "Statistics": stats, "Memory": {"BytesAllocatedPerOperation": 0}, "Measurements": rows}
            archive_data[report] = {"Benchmarks": [benchmark]}
            record = {"caseId": label, "group": group, "type": expected["Case"]["Type"], "method": "Typed",
                      "lifetime": "Scoped", "count": None, "asynchronous": None, "library": "Zendiator",
                      "comparisonKey": expected["ComparisonKey"], "validN": n, "rawActualN": 12,
                      "statisticsOriginalValues": stats["OriginalValues"], "meanNs": stats["Mean"],
                      "medianNs": stats["Median"], "stdDevNs": 1.0, "allocatedBytes": 0, "excludedIterations": 12 - n,
                      "reportPath": str(results / report),
                      "reportSha256": hashlib.sha256(json.dumps(archive_data[report]).encode()).hexdigest().upper(),
                      "childRuntimeSha256": binary_hash, "consumerSha256": binary_hash}
            records.append(record)
            summaries.append({"Group": group, **expected["Case"], "Count": "", "Asynchronous": "",
                              "MeanNs": stats["Mean"], "MedianNs": stats["Median"], "StdDevNs": 1,
                              "AllocatedBytes": 0, "N": n})
            if n == 11:
                excluded.append({"CaseId": label, "launchIndex": 1, "iterationIndex": 12,
                                 "operations": 1, "rawNanosecondsBeforeOverheadCorrection": 12.0})
            files = {"bin/Release/net10.0/" + name: binary_hash for name in
                     ("Zendiator.dll", "Zendiator.UseCaseBenchmarks.dll")}
            archive_data[prefix + "artifact-binding.json"] = {"session": "synthetic",
                "semantic": {"revision": audit.SOURCE_SHA, "bundleFiles": files}, "bundleBeforeAndAfterVerified": True}
            archive_data[prefix + "artifact-gate/artifact-proof.json"] = {"session": "synthetic",
                "benchmarkGateCases": 394, "bundleBeforeAndAfterVerified": True}
            for relative in files:
                path = results / prefix / "bundle" / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(binary)
    save(review / "verified-results.json", {"verification": verification, "records": records})
    for path, rows in ((review / "excluded-actual-iterations.csv", excluded), (results / "all-results.csv", summaries)):
        with path.open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
    inventory = []
    with zipfile.ZipFile(review / "raw-bdn-evidence.zip", "w", zipfile.ZIP_DEFLATED) as archive:
        for name, data in archive_data.items():
            payload = json.dumps(data).encode()
            archive.writestr(name, payload)
            inventory.append({"path": name, "bytes": len(payload),
                              "sha256": hashlib.sha256(payload).hexdigest().upper()})
    save(review / "raw-archive-manifest.json", inventory)
    manifest = {"schemaVersion": 1, "sourceSha": audit.SOURCE_SHA, "sessionId": "synthetic",
                "sourceDigest": "synthetic", "results": ".local/results", "control": ".local/control",
                "pins": [{"path": path.relative_to(root).as_posix(), "bytes": path.stat().st_size,
                          "sha256": audit.sha(path)} for path in root.rglob("*") if path.is_file() and "bundle" not in path.parts]}
    return manifest, records, archive_data


class BaselineAudit(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.directory = tempfile.TemporaryDirectory()
        cls.root = Path(cls.directory.name)
        cls.manifest, cls.records, cls.archive_data = fixture(cls.root)

    @classmethod
    def tearDownClass(cls):
        cls.directory.cleanup()

    def test_full_audit_is_read_only_and_matches_raw_counts(self):
        before = {path: audit.sha(path) for path in self.root.rglob("*") if path.is_file()}
        result = audit.validate(self.root, self.manifest)
        self.assertEqual((result["cases"], result["validN"], len(result["retainedFiles"])), (360, 4042, 720))
        self.assertEqual(before, {path: audit.sha(path) for path in self.root.rglob("*") if path.is_file()})

    def test_wrong_sha_and_missing_pin_are_rejected(self):
        for change, message in (({"sourceSha": "old"}, "Wrong baseline SHA"), ({"pins": []}, "Missing required")):
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, message):
                audit.validate(self.root, {**self.manifest, **change})

    def test_changed_or_missing_retained_binary_is_rejected(self):
        path = self.root / ".local/results/runs/send/cases/0001/attempt-0001/bundle/bin/Release/net10.0/Zendiator.dll"
        original = path.read_bytes()
        try:
            path.write_bytes(b"other build")
            with self.assertRaisesRegex(ValueError, "Retained binary hash"):
                audit.validate(self.root, self.manifest)
            path.unlink()
            with self.assertRaises(FileNotFoundError):
                audit.validate(self.root, self.manifest)
        finally:
            path.write_bytes(original)

    def test_same_counts_with_wrong_workload_or_duplicate_iteration_are_rejected(self):
        original = self.archive_data["runs/send/cases/0001/attempt-0001/results/report-full.json"]["Benchmarks"][0]
        for change, message in (("workload", "Workload mismatch"), ("iteration", "measurement identities"),
                                ("statistics", "Statistics/raw mismatch")):
            benchmark = copy.deepcopy(original)
            if change == "workload":
                benchmark["Type"] = "OldWorkload"
            elif change == "iteration":
                benchmark["Measurements"][21]["IterationIndex"] = 1
            else:
                benchmark["Statistics"]["OriginalValues"][0] = 99
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, message):
                audit.verify_case(benchmark, self.records[0], {"Case": {"Type": "Zendiatorsend1", "Method": "Typed",
                                  "Lifetime": "Scoped"}, "Library": "Zendiator", "ComparisonKey": ["send", 1]})

    def test_archive_corruption_missing_member_and_duplicate_member_are_rejected(self):
        for names, expected, message in ((["raw"], b"wrong", "hash/size mismatch"),
                                         ([], b"raw", "Missing or unexpected"),
                                         (["raw", "raw"], b"raw", "Duplicate ZIP")):
            with self.subTest(names=names), tempfile.TemporaryFile() as stream:
                with zipfile.ZipFile(stream, "w") as archive:
                    with warnings.catch_warnings():
                        warnings.simplefilter("ignore", UserWarning)
                        for name in names:
                            archive.writestr(name, b"raw")
                stream.seek(0)
                with zipfile.ZipFile(stream) as archive, self.assertRaisesRegex(ValueError, message):
                    audit.verify_archive(archive, [{"path": "raw", "bytes": len(expected),
                                         "sha256": hashlib.sha256(expected).hexdigest()}])

    def test_embedded_inventory_is_checked_without_a_recursive_self_hash(self):
        inventory = [{"path": "raw", "bytes": 3, "sha256": hashlib.sha256(b"raw").hexdigest()}]
        for embedded in (inventory, []):
            with self.subTest(embedded=embedded), tempfile.TemporaryFile() as stream:
                with zipfile.ZipFile(stream, "w") as archive:
                    archive.writestr("raw", b"raw")
                    archive.writestr("raw-archive-manifest.json", json.dumps(embedded))
                stream.seek(0)
                with zipfile.ZipFile(stream) as archive:
                    if embedded == inventory:
                        audit.verify_archive(archive, inventory)
                    else:
                        with self.assertRaisesRegex(ValueError, "Embedded inventory mismatch"):
                            audit.verify_archive(archive, inventory)

    def test_path_escape_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "outside baseline root"):
            audit.within(self.root, "../other-baseline/session.json")


if __name__ == "__main__":
    unittest.main()
