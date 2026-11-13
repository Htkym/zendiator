"""Synthetic artifacts only; real process fixtures require explicit opt-in.

Run process fixtures only in an authorized slot with
ZENDIATOR_DRIVER_PROCESS_FIXTURES=1. They launch Python sleepers, never dotnet.
"""

import csv
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import patch

import run_interleaved as driver
import owned_process
from interleaved_plan import identity, library, make_plan, normalize_case, verify_log


class ProjectPlanContracts(unittest.TestCase):
    def baseline_cases(self):
        peers = ("Zendiator", "MediatRHistorical", "Mediator", "DispatchR", "Immediate", "Foundatio")
        families = [
            ("Send0", "Typed", {"Lifetime": "Scoped"}),
            ("Send5", "Typed", {"Lifetime": "Scoped"}),
            ("Send0", "ResolveSend", {"Lifetime": "Scoped"}),
            ("Send5", "ResolveSend", {"Lifetime": "Scoped"}),
            ("Send5", "ScopeK1", {"Lifetime": "Scoped"}),
            ("Notification16", "Dispatch", {}),
            ("Stream5", "Full", {"Count": 1024, "Asynchronous": False}),
            ("Stream5", "Full", {"Count": 1024, "Asynchronous": True}),
            ("Stream5", "EarlyBreak", {"Count": 16, "Asynchronous": False}),
            ("Stream5", "EarlyBreak", {"Count": 16, "Asynchronous": True}),
            ("Stream0", "Full", {"Count": 1024, "Asynchronous": True}),
            ("Stream0", "EarlyBreak", {"Count": 16, "Asynchronous": False}),
            ("Relay5", "Full", {"Count": 1024, "Asynchronous": True}),
            ("Relay5", "Full", {"Count": 1024, "Asynchronous": False}),
            ("Relay5", "EarlyBreak", {"Count": 16, "Asynchronous": False}),
            ("Relay5", "EarlyBreak", {"Count": 16, "Asynchronous": True}),
        ]
        cases = []
        for family, method, parameters in families:
            for peer in peers:
                if family == "Notification16" and peer == "Immediate":
                    continue
                case = {"Type": peer + family, "Method": method, **parameters}
                if peer == "Foundatio":
                    case["Project"] = "foundatio"
                cases.append(case)
        for peer in peers:
            prefix = "MediatR" if peer == "MediatRHistorical" else peer
            cases.append({"Type": prefix + "ShortCircuit", "Method": "WarmDispatch", "Project": "short"})
        for method in ("FirstSend", "WarmSend", "ColdOuterFailure"):
            cases.extend({"Type": f"H{peer}Benchmarks", "Method": method, "Project": "hooks"} for peer in peers)
        cases.extend({"Type": f"ZendiatorSend{depth}", "Method": "FirstSend", "Lifetime": "Scoped"} for depth in (0, 5))
        cases.append({"Type": "ZendiatorSend5", "Method": "ScopeK10", "Lifetime": "Scoped"})
        return cases

    def test_all_122_cells_rotate_across_projects_with_original_type_aliases(self):
        cases = self.baseline_cases()
        plan, keys = make_plan(cases)
        self.assertEqual((len(plan), keys), (122, 23))
        self.assertEqual({identity(row["Case"]) for row in plan}, {identity(case) for case in cases})
        groups = {}
        for row in plan:
            groups.setdefault(tuple(row["ComparisonKey"]), []).append(row["Library"])
        self.assertEqual(groups[("Send0", "Typed", "Scoped", None, None)],
                         ["Zendiator", "MediatRHistorical", "Mediator", "DispatchR", "Immediate", "Foundatio"])
        self.assertEqual(groups[("Send5", "Typed", "Scoped", None, None)],
                         ["MediatRHistorical", "Mediator", "DispatchR", "Immediate", "Foundatio", "Zendiator"])
        self.assertEqual(groups[("ShortCircuit", "WarmDispatch", None, None, None)],
                         ["Immediate", "Foundatio", "Zendiator", "MediatRHistorical", "Mediator", "DispatchR"])
        self.assertEqual(groups[("Hooks", "FirstSend", None, None, None)],
                         ["Foundatio", "Zendiator", "MediatRHistorical", "Mediator", "DispatchR", "Immediate"])

    def test_project_canonicalization_rejects_wrong_projects_and_false_hook_prefixes(self):
        case = {"Type": "ZendiatorSend0", "Method": "Typed", "Lifetime": "Scoped"}
        self.assertEqual(identity(case), identity(dict(case, Project="usecase")))
        self.assertEqual(normalize_case(case), normalize_case(dict(case, Project=None)))
        with self.assertRaisesRegex(ValueError, "duplicate cases"):
            make_plan([case, dict(case, Project="usecase")])
        for invalid in (dict(case, Project="hooks"), dict(case, Project="unknown"),
                        {"Type": "HZendiatorBenchmarks", "Method": "WarmSend"},
                        {"Type": "FoundatioShortCircuit", "Method": "WarmDispatch", "Project": "foundatio"},
                        {"Type": "HZendiatorUnknown", "Method": "WarmSend", "Project": "hooks"}):
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                make_plan([invalid])
        with self.assertRaisesRegex(ValueError, "Cannot identify one library"):
            library({"Type": "HZendiatorUnknown", "Method": "WarmSend"})

    def test_cross_project_log_keeps_exact_case_identity_and_order(self):
        plan, _ = make_plan(self.baseline_cases())
        lines = []
        for row in plan:
            case = row["Case"]
            parameters = ", ".join(f"{name}={case[name]}" for name in ("Lifetime", "Count", "Asynchronous") if name in case)
            suffix = f" [{parameters}]" if parameters else ""
            lines.append(f"// Benchmark: {case['Type']}.{case['Method']}: Comparison(){suffix}\n")
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "run.log"
            path.write_text("".join(lines), encoding="utf-8")
            verify_log(plan, path)
            for invalid in (lines[:-1], [lines[1], *lines[1:]], [lines[1], lines[0], *lines[2:]]):
                path.write_text("".join(invalid), encoding="utf-8")
                with self.assertRaises(ValueError):
                    verify_log(plan, path)


class AggregateReportContracts(unittest.TestCase):
    def analyze(self, cases, reports):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            matrix = root / "matrix.json"
            matrix.write_text(json.dumps(cases), encoding="utf-8")
            results = root / "runs/custom/results"
            results.mkdir(parents=True)
            (results / "fixture-full.json").write_text(json.dumps({"Benchmarks": reports}), encoding="utf-8")
            (results.parent / "outcome.json").write_text(json.dumps({"cases": len(cases), "failures": 0}), encoding="utf-8")
            result = subprocess.run([sys.executable, str(Path(__file__).with_name("analyze.py")),
                                     str(root), "--matrix", str(matrix)], capture_output=True, text=True)
            path = root / "all-results.csv"
            if not path.exists():
                return result, []
            with path.open(encoding="utf-8", newline="") as stream:
                return result, list(csv.DictReader(stream))

    def report(self, case):
        return {"Type": case["Type"], "Method": case["Method"], "Project": "untrusted-project",
                "Parameters": ", ".join(f"{name}={case[name]}" for name in ("Lifetime", "Count", "Asynchronous") if name in case),
                "Statistics": {"Mean": 10.0, "Median": 9.9, "N": 12, "StandardDeviation": 0.1,
                               "ConfidenceInterval": {"Lower": 9.5, "Upper": 10.5, "Margin": 0.5}},
                "Memory": {"BytesAllocatedPerOperation": 0}}

    def test_projects_come_from_declared_cases_and_confidence_is_preserved(self):
        cases = [{"Type": "ZendiatorSend0", "Method": "Typed", "Lifetime": "Scoped", "Project": "usecase"},
                 {"Type": "HZendiatorBenchmarks", "Method": "WarmSend", "Project": "hooks"},
                 {"Type": "MediatRShortCircuit", "Method": "WarmDispatch", "Project": "short"},
                 {"Type": "FoundatioStream5", "Method": "Full", "Count": 1024, "Asynchronous": False, "Project": "foundatio"}]
        result, rows = self.analyze(cases, [self.report(case) for case in cases])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual({row["Type"]: row["Project"] for row in rows},
                         {case["Type"]: case["Project"] if case["Project"] != "usecase" else "" for case in cases})
        for row in rows:
            self.assertEqual((row["MeanNs"], row["AllocatedBytes"], row["ConfidenceLowerNs"],
                              row["ConfidenceUpperNs"], row["ConfidenceMarginNs"]), ("10.0", "0", "9.5", "10.5", "0.5"))
        old = self.report(cases[0])
        old["Statistics"].pop("ConfidenceInterval")
        result, rows = self.analyze([dict(cases[0], Project=None)], [old])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((rows[0]["Project"], rows[0]["ConfidenceLowerNs"], rows[0]["ConfidenceUpperNs"]), ("", "", ""))

    def test_ambiguous_unexpected_or_duplicate_results_cannot_be_aggregated(self):
        case = {"Type": "ZendiatorSend0", "Method": "Typed", "Lifetime": "Scoped"}
        report = self.report(case)
        for cases, reports, error in (
                ([case, dict(case, Project="usecase")], [report], "Duplicate predeclared case"),
                ([case], [dict(report, Type="ZendiatorSend5")], "Unexpected BDN result"),
                ([case], [report, report], "Duplicate BDN result"),
                ([case], [dict(report, Parameters="Lifetime=Scoped, Project=hooks")], "Unexpected BDN parameters")):
            with self.subTest(error=error):
                result, _ = self.analyze(cases, reports)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(error, result.stderr)


    def test_report_reads_new_and_legacy_gate_layouts_and_prefers_new(self):
        for layout in ("legacy", "new", "mixed"):
            with self.subTest(layout=layout), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fields = ("Group", "Type", "Method", "Count", "Asynchronous", "MeanNs", "AllocatedBytes")
                with (root / "all-results.csv").open("w", encoding="utf-8", newline="") as stream:
                    writer = csv.DictWriter(stream, fieldnames=fields)
                    writer.writeheader()
                    writer.writerow(dict(zip(fields, ("relay", "ZendiatorRelay5", "Full", 16, False, 42, 0))))
                driver.write_json(root / "runs/send/run-info.json", {"revision": "synthetic", "sdk": "10.0.404"})
                for kind in (("legacy", "new") if layout == "mixed" else (layout,)):
                    gate_root = "gates/usecase/" if kind == "new" else ""
                    driver.write_json(root / ("runs/send/" + gate_root + "manifest.json"), {
                        "runtime": ".NET 10.0.14",
                        "packageVersions": {"MediatR": "15.0.0" if kind == "new" else "14.2.0"}})
                    driver.write_json(root / ("runs/relay/" + gate_root + "correctness.json"), [{
                        "suite": "RelayTiming", "library": "Zendiator", "behaviors": 5,
                        "eventsAtCreation": kind == "legacy", "eventsAtEnumerator": False,
                        "failureStage": "move-call" if kind == "new" else "creation"}])
                result = subprocess.run([sys.executable, str(Path(__file__).with_name("make_report.py")), str(root)],
                                        capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, result.stderr)
                report = (root / "RESULT.ja.md").read_text(encoding="utf-8")
                self.assertIn("| Zendiator | 5 |", report)
                if layout != "legacy":
                    self.assertIn("MediatR 15.0.0", report)
                    self.assertNotIn("MediatR 14.2.0", report)
                    self.assertIn("MoveNextAsync", report)
                else:
                    self.assertIn("MediatR 14.2.0", report)


class ArtifactContracts(unittest.TestCase):
    def test_missing_process_evidence_blocks_resume_before_child_evidence_exists(self):
        with tempfile.TemporaryDirectory() as directory:
            case = Path(directory)
            (case / "attempt-0001").mkdir()
            with self.assertRaisesRegex(RuntimeError, "No process containment evidence"):
                driver.check_abandoned(case)

    def test_legacy_and_unverified_markers_block_resume_even_if_parent_is_dead(self):
        with tempfile.TemporaryDirectory() as directory:
            case = Path(directory)
            attempt = case / "attempt-0001"
            attempt.mkdir()
            marker = attempt / "run.log.process.json"
            for record in ({"pid": 999999, "state": "interrupted"},
                           {"schemaVersion": 2, "pid": 999999, "state": "exited", "cleanupVerified": False}):
                driver.write_json(marker, record)
                with self.subTest(record=record), self.assertRaisesRegex(RuntimeError, "termination is unverified"):
                    driver.check_abandoned(case)

    def test_verified_interruption_can_resume_without_child_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            case = Path(directory)
            attempt = case / "attempt-0001"
            attempt.mkdir()
            driver.write_json(attempt / "run.log.process.json",
                              {"schemaVersion": 2, "state": "interrupted", "cleanupVerified": True})
            driver.check_abandoned(case)

    def test_unverified_gate_or_build_blocks_resume_before_completed_case_skip(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            driver.write_json(root / "restore.log.process.json",
                              {"schemaVersion": 2, "state": "exited", "cleanupVerified": True})
            driver.write_json(root / "build.log.process.json", {"schemaVersion": 2, "state": "running"})
            with self.assertRaisesRegex(RuntimeError, "termination is unverified"):
                driver.check_resume_ownership(root)

    def _firstsend_artifact(self, group, actual):
        case = {"Type": "ZendiatorSend0", "Method": "FirstSend", "Lifetime": "Scoped"}
        plan, _ = make_plan([case])
        attempt = group / "cases/0001/attempt-0001"
        (attempt / "results").mkdir(parents=True)
        (attempt / "results/fixture-full.json").write_text('{}')
        (attempt / "child-fixture.json").write_text('{}')
        log = "// Benchmark: ZendiatorSend0.FirstSend: Comparison(InvocationCount=16384, UnrollFactor=1) [Lifetime=Scoped]\n"
        log += "".join(f"WorkloadActual {i}: 16384 op, 1 ns\n" for i in range(12)) if actual else ""
        (attempt / "run.log").write_text(log, encoding="utf-8")
        record = {"attempt": attempt.name, "childSha256": "product", "childAssemblies": {"Zendiator": "product"}, "artifactGate": {"proofSha256": "fixture"}}
        session = {"revision": "fixture", "sdk": "fixture", "sourceDigest": "fixture", "runtime": "fixture", "artifactGateProtocol": "fixture"}
        return case, plan, record, session

    def test_firstsend_requires_actual_operations_for_custom_and_send_groups(self):
        for name in ("custom",):
            with self.subTest(group=name), tempfile.TemporaryDirectory() as directory:
                group = Path(directory) / name
                case, plan, record, session = self._firstsend_artifact(group, False)
                with patch.object(driver, "completed", return_value=record):
                    with self.assertRaisesRegex(ValueError, "actual-operation evidence"):
                        driver.finish_group(group, plan, [case], session)
                self.assertFalse((group / "run-info.json").exists())

    def test_valid_firstsend_operations_are_recorded_for_custom_group(self):
        with tempfile.TemporaryDirectory() as directory:
            group = Path(directory) / "custom"
            case, plan, record, session = self._firstsend_artifact(group, True)
            with patch.object(driver, "completed", return_value=record):
                driver.finish_group(group, plan, [case], session)
            info = json.loads((group / "run-info.json").read_text())
            self.assertEqual(info["firstSendActual"]["actualRows"], 12)
            self.assertEqual(info["firstSendActual"]["operationsPerActualRow"], 16384)

    def test_cold_outer_failure_requires_twelve_complete_fixed_invocation_rows(self):
        case = {"Type": "HZendiatorBenchmarks", "Method": "ColdOuterFailure", "Project": "hooks"}
        header = "// Benchmark: HZendiatorBenchmarks.ColdOuterFailure: Comparison(InvocationCount=16384, UnrollFactor=1)\n"
        rows = [f"WorkloadActual {index}: 16384 op, 1 ns\n" for index in range(1, 13)]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "run.log"
            path.write_text(header + "".join(rows), encoding="utf-8")
            proof = driver.check_firstsend(path, [case])
            self.assertEqual((proof["cases"], proof["actualRows"], proof["operationsPerActualRow"]), (1, 12, 16384))
            for invalid in (rows[:-1], [rows[0].replace("16384", "1"), *rows[1:]]):
                path.write_text(header + "".join(invalid), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "actual-operation evidence"):
                    driver.check_firstsend(path, [case])

    def test_explicit_null_optional_parameters_match_omitted_plan_and_analysis(self):
        omitted = {"Type": "ZendiatorNotification1", "Method": "Dispatch"}
        explicit = dict(omitted, Lifetime=None, Count=None, Asynchronous=None)
        self.assertEqual(make_plan([explicit]), make_plan([omitted]))
        self.assertIn("Count", explicit)  # Normalization does not mutate the caller's matrix.
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            matrix = root / "matrix.json"
            matrix.write_text(json.dumps([explicit]))
            results = root / "runs/custom/results"
            results.mkdir(parents=True)
            (results / "fixture-full.json").write_text(json.dumps({"Benchmarks": [dict(omitted, Parameters="")]}))
            driver.write_json(root / "runs/custom/outcome.json", {"cases": 1, "failures": 0})
            result = subprocess.run([sys.executable, str(Path(__file__).with_name("analyze.py")),
                                     str(root), "--matrix", str(matrix)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("custom: 1/1 cases", result.stdout)
            with (root / "all-results.csv").open(newline="") as file:
                row = next(csv.DictReader(file))
            self.assertEqual((row["Lifetime"], row["Count"], row["Asynchronous"]), ("", "", ""))


class SendDiagnosticContracts(unittest.TestCase):
    def test_diagnostic_rejects_other_workloads_and_resumable_partial_runs(self):
        case = {"Type": "ZendiatorSend0", "Method": "Typed", "Lifetime": "Scoped"}
        for group, cases, resume, partial in (("send", [case], False, None), ("custom", [dict(case, Method="ScopeK1")], False, None),
                                             ("custom", [case], True, None), ("custom", [case], False, 1)):
            with self.subTest(group=group, cases=cases), self.assertRaisesRegex(ValueError, "Send diagnostic"):
                driver.send_diagnostic_profile("fixed", group, cases, resume, partial)

    def test_fixed_cli_misconfiguration_or_short_report_cannot_pass_diagnostic_gate(self):
        count = driver.SEND_DIAGNOSTIC_INVOCATIONS
        display = f"Comparison(Affinity=00000001, InvocationCount={count}, IterationCount=12, IterationTime=500ms, LaunchCount=1, WarmupCount=20)"
        points = [{"IterationMode": "Workload", "IterationStage": stage, "Operations": count, "Nanoseconds": 500000000}
                  for stage, rows in (("Warmup", 20), ("Actual", 12)) for _ in range(rows)]
        points.insert(0, {"IterationMode": "Overhead", "IterationStage": "Actual", "Operations": count, "Nanoseconds": 250000000})
        result = {"DisplayInfo": display, "Measurements": points}
        self.assertEqual(driver.check_send_diagnostic(result, "fixed")["actualRows"], 12)
        for bad in ({"DisplayInfo": display.replace("InvocationCount=" + str(count), "InvocationCount=16"), "Measurements": points},
                    {"DisplayInfo": display, "Measurements": points[:-1]},
                    {"DisplayInfo": display, "Measurements": points + [dict(points[1], IterationStage="Pilot")]},
                    {"DisplayInfo": display, "Measurements": [dict(x, Operations=16) for x in points]}):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                driver.check_send_diagnostic(bad, "fixed")


class ProcessIdentityContracts(unittest.TestCase):
    def test_current_process_identity_is_read_without_signals(self):
        identity = owned_process.process_identity(os.getpid())
        self.assertTrue(driver.process_alive(identity))
        prefix, separator, last = identity["startToken"].rpartition(":")
        reused = dict(identity, startToken=(prefix + separator if separator else "") + str(int(last) + 1))
        self.assertFalse(driver.process_alive(reused))


    def test_linux_stat_parser_distinguishes_zombie_and_terminated_with_same_identity(self):
        for state in ("Z", "X", "R"):
            fields = [state] + ["0"] * 18 + ["42"] + ["0"] * 12
            snapshot = owned_process.linux_snapshot_from_stat(123, "123 (name with ) brackets) " + " ".join(fields), "00000000-0000-0000-0000-000000000001")
            self.assertEqual(snapshot["startToken"], "00000000-0000-0000-0000-000000000001:42")
            self.assertEqual(snapshot["state"], "running" if state == "R" else "terminated")

    def test_unidentifiable_process_or_missing_identity_blocks_resume(self):
        for identity in (None, {}, {"pid": 123, "platform": "linux"}, {"pid": 123, "platform": "windows", "startToken": "unknown"}):
            with self.subTest(identity=identity), self.assertRaisesRegex(RuntimeError, "identity"):
                driver.process_alive(identity)
        identity = {"pid": 123, "platform": "windows" if os.name == "nt" else "linux",
                    "startToken": "638000000000000123" if os.name == "nt" else "00000000-0000-0000-0000-000000000001:123"}
        with patch.object(owned_process, "process_snapshot", side_effect=RuntimeError("identity denied")):
            with self.assertRaisesRegex(RuntimeError, "identity denied"):
                driver.process_alive(identity)



@unittest.skipUnless(os.environ.get("ZENDIATOR_DRIVER_PROCESS_FIXTURES") == "1", "process fixture opt-in required")
class ProcessContainment(unittest.TestCase):
    def test_direct_parent_exit_does_not_leave_owned_descendant_or_stop_unrelated_process(self):
        unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(30)"])
        try:
            with tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                code = "import subprocess,sys; subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)'])"
                with self.assertRaisesRegex(RuntimeError, "live owned descendants"):
                    driver.run_logged([sys.executable, "-c", code], root / "run.log", os.environ.copy())
                marker = json.loads((root / "run.log.process.json").read_text())
                self.assertTrue(marker["cleanupVerified"])
                self.assertEqual(marker["state"], "failed")
                self.assertIsNone(unrelated.poll())
        finally:
            unrelated.terminate()
            unrelated.wait(timeout=10)

    @unittest.skipUnless(sys.platform.startswith("linux"), "POSIX SIGINT reproduction")
    def test_sigint_before_child_evidence_cleans_session_and_allows_verified_resume(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            ready = root / "ready"
            child_code = ("import pathlib,subprocess,sys,time; "
                          "subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)']); "
                          f"pathlib.Path({str(ready)!r}).write_text('ready'); time.sleep(30)")
            script = root / "interrupt_fixture.py"
            script.write_text("import os,sys\nfrom pathlib import Path\n"
                              f"sys.path.insert(0, {str(Path(__file__).parent)!r})\nimport run_interleaved as d\n"
                              f"d.run_logged([sys.executable,'-c',{child_code!r}], Path({str(root / 'run.log')!r}), os.environ.copy())\n")
            process = subprocess.Popen([sys.executable, str(script)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            try:
                deadline = time.monotonic() + 10
                while not ready.exists() and process.poll() is None and time.monotonic() < deadline:
                    time.sleep(0.05)
                self.assertTrue(ready.exists())
                process.send_signal(signal.SIGINT)
                process.wait(timeout=25)
                marker = json.loads((root / "run.log.process.json").read_text())
                self.assertTrue(marker["cleanupVerified"])
                self.assertEqual(marker["state"], "interrupted")
                self.assertFalse(list(root.glob("child-*.json")))
                driver.check_owned_markers(root)
            finally:
                if process.poll() is None:
                    process.send_signal(signal.SIGINT)
                    process.wait(timeout=25)


if __name__ == "__main__":
    unittest.main()
