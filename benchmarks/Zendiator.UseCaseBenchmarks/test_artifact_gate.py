"""Synthetic, non-.NET files exercise proof/bundle rejection and cache boundaries.

The mocked gate never executes an application. Retained real-consumer activation,
Windows Job behavior and 394 checks require a separately authorized validation.
"""

import copy
import json
import shutil
import tempfile
import unittest
import uuid
from pathlib import Path

import artifact_gate as gate


class ArtifactGateContracts(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "repo/benchmarks/Zendiator.UseCaseBenchmarks"
        self.build = self.project / "bin/Release/net10.0/Zendiator.UseCaseBenchmarks-1"
        self.binaries = self.build / "bin/Release/net10.0"
        self.framework = self.root / "runtime/shared/Microsoft.NETCore.App/10.0.12"
        self.binaries.mkdir(parents=True)
        self.framework.mkdir(parents=True)
        self.host = self.root / "runtime/dotnet"
        self.host.write_text("synthetic host; never execute")
        for name in ("ArtifactGate.cs", "ArtifactEvidence.cs", "Program.cs", "ChildEvidence.cs"):
            (self.project / name).write_text("synthetic implementation fingerprint")
        self.generated_project = self.build / "Zendiator.UseCaseBenchmarks-1.csproj"
        self.generated_source = self.build / "Zendiator.UseCaseBenchmarks-1.notcs"
        self.generated_project.write_text("<Project />")
        self.generated_source.write_text("synthetic BDN host StartupObject")
        self.session = {"sessionId": "session-A", "revision": "revision-A", "sourceDigest": "source-A",
                        "sdk": "10.0.401", "artifactGateProtocol": gate.PROTOCOL}
        self.loads = []
        names = sorted(gate.REQUIRED | {"MediatR", "MediatR.Contracts", "Zendiator.UseCaseBenchmarks-1"})
        for name in names:
            path = (self.framework if name == "System.Private.CoreLib" else self.binaries) / (name + ".dll")
            path.write_text("synthetic DLL: " + name)
            self.loads.append({"name": name, "fullName": name + ", Version=0.3.0.0",
                               "path": str(path), "mvid": str(uuid.uuid5(uuid.NAMESPACE_DNS, name)), "sha256": gate.sha(path)})
        self.runtime_config = self.binaries / "Zendiator.UseCaseBenchmarks-1.runtimeconfig.json"
        self.deps = self.binaries / "Zendiator.UseCaseBenchmarks-1.deps.json"
        self.framework_deps = self.framework / "Microsoft.NETCore.App.deps.json"
        gate.write(self.runtime_config, {"runtimeOptions": {"framework": {"name": "Microsoft.NETCore.App", "version": "10.0.0"}}})
        gate.write(self.deps, {"targets": {"synthetic": {}}})
        gate.write(self.framework_deps, {"targets": {"synthetic framework": {}}})
        self.native = self.framework / "coreclr.dll"
        self.native.write_text("synthetic native runtime")
        file = lambda path: {"path": str(path), "sha256": gate.sha(path)}
        self.runtime = {"framework": ".NET 10.0.12", "frameworkVersion": "10.0.12", "architecture": "X64",
                        "osArchitecture": "X64", "frameworkDirectory": str(self.framework), "hostPath": str(self.host),
                        "hostSha256": gate.sha(self.host), "serverGC": False, "latencyMode": "Interactive",
                        "runtimeConfig": file(self.runtime_config), "deps": file(self.deps),
                        "appDeps": [file(self.deps), file(self.framework_deps)], "nativeRuntime": [file(self.native)],
                        "controls": {name: None for name in gate.CONTROLS}}
        self.gate_calls = []

    def case(self, ordinal=1, type="ZendiatorNotification1", pid=111):
        row = {"Library": "Zendiator" if type.startswith("Zendiator") else "MediatRHistorical",
               "Case": {"Type": type, "Method": "Dispatch"}}
        attempt = self.root / f"results/runs/custom/cases/{ordinal:04d}/attempt-0001"
        attempt.mkdir(parents=True)
        initial_path = attempt / "child-setup.json"
        gate.write(initial_path, {"pid": pid, "scenario": type, "lifetime": "Default", "assemblies": self.loads})
        final = {"schemaVersion": 1, "pid": pid, "scenario": type, "lifetime": "Default",
                 "phase": "process-exit-after-workload", "initialEvidenceSha256": gate.sha(initial_path),
                 "runtime": self.runtime["framework"], "runtimeEvidence": copy.deepcopy(self.runtime),
                 "assemblies": copy.deepcopy(self.loads)}
        gate.write(attempt / f"loaded-final-{pid}.json", final)
        gate.write(attempt / "build-artifacts.json", {"schemaVersion": 1, "reports": [{
            "type": type, "method": "Dispatch", "buildSuccess": True, "paths": {
                "buildArtifactsDirectoryPath": str(self.build), "binariesDirectoryPath": str(self.binaries),
                "projectFilePath": str(self.generated_project), "programCodePath": str(self.generated_source),
                "executablePath": str(self.binaries / "Zendiator.UseCaseBenchmarks-1.dll")}}]})
        return row, attempt

    def mocked_gate(self, args, log, env, cwd):
        # Assert the actual proposed activation command; never execute the synthetic dotnet file.
        self.assertEqual(args[0], str(self.host))
        self.assertEqual(args[1], "exec")
        self.assertIn("--runtimeconfig", args)
        self.assertIn("--depsfile", args)
        self.assertIn("--fx-version", args)
        self.assertEqual(args[args.index("--roll-forward") + 1], "Disable")
        self.assertEqual(Path(args[-3]).name, "Zendiator.UseCaseBenchmarks.dll")
        self.assertEqual(args[-2], "--artifact-gate")
        self.assertEqual(env["COLD_CAPTURE_CHILD"], "0")
        self.assertNotIn("COLD_SKIP_GATE", env)
        self.gate_calls.append(args)
        request_path = Path(args[-1])
        request = gate.read(request_path)
        self.assertEqual(env["COLD_EVIDENCE_DIR"], request["output"])
        self.assertEqual(gate.tree_hashes(request["bundleRoot"]), request["bundleFiles"])
        outcomes = Path(request["output"]) / "correctness.json"
        gate.write(outcomes, [{"status": "Passed"} for _ in range(394)])
        proof = {key: request[key] for key in ("protocol", "session", "case", "childPid", "childProofSha256",
                 "gateImplementationSha256", "gateDriverSha256", "bundleFiles")}
        proof.update(schemaVersion=1, benchmarkGateCases=394, requestHash=gate.sha(request_path),
                     correctnessSha256=gate.sha(outcomes), bundleBeforeAndAfterVerified=True,
                     assemblies=request["expectedAssemblies"], runtimeEvidence=request["expectedRuntime"])
        gate.write(Path(request["output"]) / "artifact-proof.json", proof)

    def validate(self, row, attempt):
        return gate.ensure_artifact_gate(row, attempt, self.session, self.project, self.mocked_gate,
                                        lambda: {"COLD_SKIP_GATE": "1"})

    def test_missing_post_workload_snapshot_is_rejected(self):
        row, attempt = self.case()
        next(attempt.glob("loaded-final-*.json")).unlink()
        with self.assertRaisesRegex(ValueError, "post-workload"):
            self.validate(row, attempt)
        self.assertFalse(self.gate_calls)

    def test_consumer_entry_uses_saved_config_and_keeps_generated_host(self):
        row, attempt = self.case()
        result = self.validate(row, attempt)
        self.assertEqual(len(self.gate_calls), 1)
        self.assertFalse(result["proofReused"])
        request = gate.read(attempt / "artifact-gate-request.json")
        self.assertTrue(Path(request["consumerPath"]).is_relative_to(attempt / "bundle"))
        self.assertIn("Zendiator.UseCaseBenchmarks-1", [item["name"] for item in request["expectedAssemblies"]])

    def test_long_generated_paths_are_archived_and_hashed_completely(self):
        # TemporaryDirectory's ordinary rmtree also needs an extended path on Windows.
        self.assertTrue(self.root.resolve().is_relative_to(Path(tempfile.gettempdir()).resolve()))
        self.addCleanup(shutil.rmtree, gate.filesystem_path(self.root))
        leaf = self.build / ("generated-" + "a" * 100) / ("generator-" + "b" * 100) / "artifact.txt"
        gate.filesystem_path(leaf.parent).mkdir(parents=True)
        gate.filesystem_path(leaf).write_text("retained generated artifact")
        self.assertGreater(len(str(leaf)), 260)
        row, attempt = self.case()
        self.validate(row, attempt)
        request = gate.read(attempt / "artifact-gate-request.json")
        relative = leaf.relative_to(self.build).as_posix()
        self.assertIn(relative, request["bundleFiles"])
        self.assertEqual(gate.sha(attempt / "bundle" / relative), gate.sha(leaf))

    def test_post_workload_late_binding_is_included_in_the_gate_map(self):
        row, attempt = self.case()
        path = self.framework / "System.LateBinding.dll"
        path.write_text("synthetic late binding")
        final_path = next(attempt.glob("loaded-final-*.json"))
        final = gate.read(final_path)
        final["assemblies"].append({"name": "System.LateBinding", "fullName": "System.LateBinding, Version=10.0.0.0",
                                    "path": str(path), "mvid": str(uuid.uuid4()), "sha256": gate.sha(path)})
        gate.write(final_path, final)
        self.validate(row, attempt)
        request = gate.read(attempt / "artifact-gate-request.json")
        self.assertIn("System.LateBinding", [item["name"] for item in request["expectedAssemblies"]])

    def test_resume_reads_saved_runtime_config_after_mutable_bdn_files_disappear(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        self.runtime_config.unlink()
        self.deps.unlink()
        (self.binaries / "Zendiator.UseCaseBenchmarks.dll").unlink()
        self.assertTrue(gate.check_artifact_binding(row, attempt, self.session, self.project)["proofSha256"])

    def test_exact_map_reuses_proof_but_each_child_has_its_own_binding(self):
        first_row, first = self.case(1, pid=111)
        self.validate(first_row, first)
        second_row, second = self.case(2, "MediatRHistoricalNotification1", pid=222)
        result = self.validate(second_row, second)
        self.assertTrue(result["proofReused"])
        self.assertEqual(len(self.gate_calls), 1)
        binding = gate.read(second / "artifact-binding.json")
        self.assertEqual(binding["childPid"], 222)
        self.assertEqual(binding["case"], gate.canonical(second_row["Case"]))

    def test_generated_host_change_changes_conservative_cache_key(self):
        row, first = self.case(1)
        before = self.validate(row, first)["key"]
        host = self.binaries / "Zendiator.UseCaseBenchmarks-1.dll"
        host.write_text("changed synthetic BDN host")
        next(item for item in self.loads if item["name"] == "Zendiator.UseCaseBenchmarks-1")["sha256"] = gate.sha(host)
        row, second = self.case(2, pid=222)
        result = self.validate(row, second)
        self.assertNotEqual(before, result["key"])
        self.assertEqual(len(self.gate_calls), 2)

    def test_gate_implementation_version_participates_in_cache_key(self):
        row, first = self.case(1)
        before = self.validate(row, first)["key"]
        (self.project / "ArtifactGate.cs").write_text("changed gate implementation")
        row, second = self.case(2, pid=222)
        self.assertNotEqual(before, self.validate(row, second)["key"])
        self.assertEqual(len(self.gate_calls), 2)

    def test_corrupt_bundle_is_rejected_after_successful_gate(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        request = gate.read(attempt / "artifact-gate-request.json")
        Path(request["consumerPath"]).write_text("substituted consumer")
        with self.assertRaisesRegex(ValueError, "bundle|identity|substitut"):
            gate.check_artifact_binding(row, attempt, self.session, self.project)

    def test_deleted_proof_is_rejected_instead_of_silently_regated(self):
        row, first = self.case(1)
        self.validate(row, first)
        proof = Path(gate.read(first / "artifact-binding.json")["proofPath"])
        proof.unlink()
        row, second = self.case(2, pid=222)
        with self.assertRaises(FileNotFoundError):
            self.validate(row, second)
        self.assertEqual(len(self.gate_calls), 1)

    def test_other_session_cannot_borrow_case_proof(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        changed = dict(self.session, sessionId="session-B")
        with self.assertRaisesRegex(ValueError, "session"):
            gate.check_artifact_binding(row, attempt, changed, self.project)

    def test_runtime_or_native_binary_change_is_rejected(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        self.native.write_text("substituted native runtime")
        with self.assertRaisesRegex(ValueError, "runtime"):
            gate.check_artifact_binding(row, attempt, self.session, self.project)

    def test_gate_architecture_substitution_is_rejected_before_completion(self):
        row, attempt = self.case()
        def wrong_architecture(args, log, env, cwd):
            self.mocked_gate(args, log, env, cwd)
            request = gate.read(args[-1])
            path = Path(request["output"]) / "artifact-proof.json"
            proof = gate.read(path)
            proof["runtimeEvidence"]["architecture"] = "Arm64"
            gate.write(path, proof)
        with self.assertRaisesRegex(ValueError, "architecture"):
            gate.ensure_artifact_gate(row, attempt, self.session, self.project, wrong_architecture, lambda: {})
        self.assertFalse((attempt / "artifact-binding.json").exists())

    def test_gate_count_is_not_a_full_suite_or_contract_guarantee(self):
        row, attempt = self.case()
        result = self.validate(row, attempt)
        self.assertIn("notification-contract/full-suite evidence remains separate", result["scope"])
        path = Path(gate.read(attempt / "artifact-binding.json")["proofPath"])
        proof = gate.read(path)
        proof["benchmarkGateCases"] = 393
        gate.write(path, proof)
        with self.assertRaisesRegex(ValueError, "proof"):
            gate.check_artifact_binding(row, attempt, self.session, self.project)


if __name__ == "__main__":
    unittest.main()
