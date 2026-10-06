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
from interleaved_plan import make_plan


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
