"""Synthetic, non-.NET files exercise proof/bundle rejection and cache boundaries.

The mocked gate never executes an application. Retained real-consumer activation,
Windows Job behavior and 389+96 checks require a separately authorized validation.
"""

import copy
import json
import itertools
import os
import shutil
import tempfile
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch

import artifact_gate as gate
import run_interleaved as driver
from interleaved_plan import make_plan


class SyntheticArtifactGateFixture(unittest.TestCase):
    consumer = "Zendiator.UseCaseBenchmarks"

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "repo/benchmarks" / self.consumer
        self.build = self.project / "bin/Release/net10.0" / (self.consumer + "-1")
        self.binaries = self.build / "bin/Release/net10.0"
        self.framework = self.root / "runtime/shared/Microsoft.NETCore.App/10.0.12"
        self.binaries.mkdir(parents=True)
        self.framework.mkdir(parents=True)
        self.host = self.root / "runtime/dotnet"
        self.host.write_text("synthetic host; never execute")
        for name in ("ArtifactGate.cs", "ArtifactEvidence.cs", "Program.cs", "ChildEvidence.cs", "StructUpstreamGate.cs"):
            (self.project / name).write_text("synthetic implementation fingerprint")
        self.generated_project = self.build / (self.consumer + "-1.csproj")
        self.generated_source = self.build / (self.consumer + "-1.notcs")
        self.generated_project.write_text("<Project />")
        self.generated_source.write_text("synthetic BDN host StartupObject")
        self.session = {"sessionId": "session-A", "revision": "revision-A", "sourceDigest": "source-A",
                        "sdk": "10.0.401", "artifactGateProtocol": gate.PROTOCOL}
        self.loads = []
        names = sorted((gate.REQUIRED - {"Zendiator.UseCaseBenchmarks"}) |
                       {self.consumer, "MediatR", "MediatR.Contracts", self.consumer + "-1"})
        for name in names:
            path = (self.framework if name == "System.Private.CoreLib" else self.binaries) / (name + ".dll")
            path.write_text("synthetic DLL: " + name)
            self.loads.append({"name": name, "fullName": name + ", Version=0.3.0.0",
                               "path": str(path), "mvid": str(uuid.uuid5(uuid.NAMESPACE_DNS, name)), "sha256": gate.sha(path)})
        self.runtime_config = self.binaries / (self.consumer + "-1.runtimeconfig.json")
        self.deps = self.binaries / (self.consumer + "-1.deps.json")
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

    def case(self, ordinal=1, type="ZendiatorNotification1", pid=111, results=None, method="Dispatch", project=None):
        row = {"Library": "Zendiator" if type.startswith("Zendiator") else "MediatRHistorical",
               "Case": {"Type": type, "Method": method}}
        if project is not None:
            row["Case"]["Project"] = project
        results = results if results is not None else self.root / "results"
        attempt = results / f"runs/custom/cases/{ordinal:04d}/attempt-0001"
        gate.filesystem_path(attempt).mkdir(parents=True)
        initial_path = attempt / "child-setup.json"
        identity = {"pid": pid, "platform": "windows" if os.name == "nt" else "linux", "startToken": str(638000000000000000 + pid) if os.name == "nt" else "00000000-0000-0000-0000-000000000001:" + str(pid)}
        gate.write(initial_path, {"pid": pid, "processIdentity": identity, "scenario": type, "lifetime": "Default", "assemblies": self.loads})
        final = {"schemaVersion": 1, "pid": pid, "processIdentity": identity, "scenario": type, "lifetime": "Default",
                 "phase": "process-exit-after-workload", "initialEvidenceSha256": gate.sha(initial_path),
                 "runtime": self.runtime["framework"], "runtimeEvidence": copy.deepcopy(self.runtime),
                 "assemblies": copy.deepcopy(self.loads)}
        gate.write(attempt / f"loaded-final-{pid}.json", final)
        gate.write(attempt / "build-artifacts.json", {"schemaVersion": 1, "reports": [{
            "type": type, "method": method, "buildSuccess": True, "paths": {
                "buildArtifactsDirectoryPath": str(self.build), "binariesDirectoryPath": str(self.binaries),
                "projectFilePath": str(self.generated_project), "programCodePath": str(self.generated_source),
                "executablePath": str(self.binaries / (self.consumer + "-1.dll"))}}]})
        return row, attempt

    def mocked_gate(self, args, log, env, cwd, usecase=False):
        # Supply a synthetic proof. Actual saved-consumer activation is covered by the authorized smoke.
        self.gate_calls.append(args)
        request_path = Path(args[-1])
        request = gate.read(request_path)
        outcomes = Path(request["output"]) / "correctness.json"
        if request.get("benchmarkSuite", "usecase") != "usecase" and not usecase:
            rows = [dict(status="Passed", type="Competitive.ShortCircuit." + name + "ShortCircuit", method="WarmDispatch")
                    for name in ("Zendiator", "Immediate", "MediatR", "Mediator", "DispatchR", "Foundatio")]
            gate.write(outcomes, rows)
            gate.write(outcomes.parent / "details.json", {"fixture": "six native short-circuit entries"})
            proof = {key: request[key] for key in ("protocol", "session", "case", "childPid", "childProofSha256",
                     "gateImplementationSha256", "gateDriverSha256", "bundleFiles", "consumerName", "benchmarkSuite")}
            proof.update(schemaVersion=1, benchmarkGateCases=6, requestHash=gate.sha(request_path),
                         correctnessSha256=gate.sha(outcomes), bundleBeforeAndAfterVerified=True,
                         assemblies=request["expectedAssemblies"], runtimeEvidence=request["expectedRuntime"],
                         semanticFiles=gate.tree_hashes(outcomes.parent))
            gate.write(outcomes.parent / "artifact-proof.json", proof)
            return
        gate.write(outcomes, [{"status": "Passed"} for _ in range(389)])
        proof = {key: request[key] for key in ("protocol", "session", "case", "childPid", "childProofSha256",
                 "gateImplementationSha256", "gateDriverSha256", "bundleFiles")}
        proof.update(schemaVersion=1, benchmarkGateCases=389, requestHash=gate.sha(request_path),
                     correctnessSha256=gate.sha(outcomes), bundleBeforeAndAfterVerified=True,
                     assemblies=request["expectedAssemblies"], runtimeEvidence=request["expectedRuntime"])
        extra = Path(request["output"]) / "struct-upstream"
        cases = extra / "struct-upstream-correctness.json"
        types = extra / "struct-upstream-driver-identification.json"
        gate.write(cases, [dict(name=name, count=count, asynchronous=asynchronous, early=early, typed=typed,
                               status="Passed", Handlers=1, Items=min(1, count) if early else count, Disposals=1)
                           for name, count, asynchronous, early, typed in itertools.product(
                               ("Stream0", "Stream5", "Relay5"), (0, 1, 16, 1024), (False, True), (False, True), (False, True))])
        gate.write(types, [dict(Type=name, Library="Zendiator", Cases=56) for name in
                           ("ZendiatorStructUpstreamStream0", "ZendiatorStructUpstreamStream5", "ZendiatorStructUpstreamRelay5")])
        implementation = request["structUpstreamGateImplementationSha256"] or "A" * 64
        proof.update(structUpstreamGateImplementationSha256=implementation,
                     structUpstreamGate=dict(schemaVersion=1, cases=96, driverTypes=3, implementationSha256=implementation,
                         correctnessPath="struct-upstream/struct-upstream-correctness.json", correctnessSha256=gate.sha(cases),
                         driverIdentificationPath="struct-upstream/struct-upstream-driver-identification.json", driverIdentificationSha256=gate.sha(types)))
        gate.write(Path(request["output"]) / "artifact-proof.json", proof)

    def validate(self, row, attempt):
        return gate.ensure_artifact_gate(row, attempt, self.session, self.project, self.mocked_gate,
                                        lambda: {"COLD_SKIP_GATE": "1"})


class ArtifactGateContracts(SyntheticArtifactGateFixture):
    def test_missing_post_workload_snapshot_is_rejected(self):
        row, attempt = self.case()
        next(attempt.glob("loaded-final-*.json")).unlink()
        with self.assertRaisesRegex(ValueError, "post-workload"):
            self.validate(row, attempt)
        self.assertFalse(self.gate_calls)

    def test_missing_process_identity_is_rejected(self):
        row, attempt = self.case()
        initial_path = next(attempt.glob("child-*.json"))
        initial = gate.read(initial_path)
        initial.pop("processIdentity")
        gate.write(initial_path, initial)
        final_path = next(attempt.glob("loaded-final-*.json"))
        final = gate.read(final_path)
        final["initialEvidenceSha256"] = gate.sha(initial_path)
        gate.write(final_path, final)
        with self.assertRaisesRegex(ValueError, "process identity"):
            self.validate(row, attempt)
        self.assertFalse(self.gate_calls)

    def test_same_pid_different_start_identity_is_rejected(self):
        row, attempt = self.case()
        final_path = next(attempt.glob("loaded-final-*.json"))
        final = gate.read(final_path)
        final["processIdentity"]["startToken"] = "different-start-same-pid"
        gate.write(final_path, final)
        with self.assertRaisesRegex(ValueError, "process identity"):
            self.validate(row, attempt)
        self.assertFalse(self.gate_calls)

    def test_consumer_entry_uses_saved_config_and_keeps_generated_host(self):
        row, attempt = self.case()
        path = self.framework / "System.LateBinding.dll"
        path.write_text("synthetic late binding")
        final_path = next(attempt.glob("loaded-final-*.json"))
        final = gate.read(final_path)
        final["assemblies"].append({"name": "System.LateBinding", "fullName": "System.LateBinding, Version=10.0.0.0",
                                    "path": str(path), "mvid": str(uuid.uuid4()), "sha256": gate.sha(path)})
        gate.write(final_path, final)
        result = self.validate(row, attempt)
        self.assertEqual(len(self.gate_calls), 1)
        self.assertFalse(result["proofReused"])
        request = gate.read(attempt / "artifact-gate-request.json")
        self.assertTrue(Path(request["consumerPath"]).is_relative_to(attempt / "bundle"))
        self.assertIn("Zendiator.UseCaseBenchmarks-1", [item["name"] for item in request["expectedAssemblies"]])
        self.assertIn("System.LateBinding", [item["name"] for item in request["expectedAssemblies"]])

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

    def test_normal_and_long_child_proofs_are_saved_read_and_resumed_with_all_guards(self):
        # Only synthetic files/proofs: no .NET consumer or process is started.
        self.assertTrue(gate.resolve_path(self.root).is_relative_to(Path(tempfile.gettempdir()).resolve()))
        self.addCleanup(shutil.rmtree, gate.filesystem_path(self.root))
        for index, results in enumerate((self.root / "short", self.root / ("session-" + "a" * 90) / ("shard-" + "b" * 70)), 1):
            with self.subTest(results=results), patch.object(driver, "PROJECT", self.project):
                row, attempt = self.case(1, type="ZendiatorNotification4", pid=111 + index, results=results)
                child = attempt / ("child-13200-ZendiatorNotification4-Default-" + "c" * 32 + ".json")
                gate.filesystem_path(attempt / "child-setup.json").replace(gate.filesystem_path(child))
                if index == 2:
                    self.assertGreaterEqual(len(str(child)), 264)
                initial = gate.read(child)
                initial["runtime"] = self.runtime["framework"]
                gate.write(child, initial)
                final_path = next(gate.glob_paths(attempt, "loaded-final-*.json"))
                final = gate.read(final_path)
                final["initialEvidenceSha256"] = gate.sha(child)
                gate.write(final_path, final)
                hashes = {item["name"]: item["sha256"] for item in self.loads}
                group = attempt.parents[2]
                gate.write(group / "correctness.json", [{"status": "Passed"} for _ in range(389)])
                self.session.update(runtimeSha256=hashes["Zendiator"], benchmarkSha256=hashes["Zendiator.UseCaseBenchmarks"],
                                    generatorSha256="A" * 64, diSha256=hashes["Microsoft.Extensions.DependencyInjection"],
                                    runtime=self.runtime["framework"], gateSha256={"custom": gate.sha(group / "correctness.json")})
                gate.write(attempt / "manifest.json", dict(self.session, assemblySha256=self.session["benchmarkSha256"],
                                                          gateProofSha256=self.session["gateSha256"]["custom"]))
                gate.write(attempt / "outcome.json", {"cases": 1, "failures": 0, "validationErrors": 0})
                gate.write(attempt / "results/fixture-full.json", {"Benchmarks": [{**row["Case"], "Parameters": ""}]})
                gate.filesystem_path(attempt / "run.log").write_text("// Benchmark: ZendiatorNotification4.Dispatch: Comparison()\n", encoding="utf-8")
                marker = attempt / "run.log.process.json"
                clean = {"schemaVersion": 2, "state": "exited", "cleanupVerified": True}
                for path in (results / "restore.log.process.json", results / "build.log.process.json", marker):
                    driver.write_json(path, clean)
                self.validate(row, attempt)
                record = driver.check_case(row, attempt, self.session)
                case_dir = attempt.parent
                driver.write_json(case_dir / "complete.json", record)
                with patch.object(driver, "process_alive", return_value=False):
                    driver.check_resume_ownership(results)
                self.assertEqual(driver.completed(row, case_dir, self.session), record)
                self.assertEqual(driver.completed(row, gate.filesystem_path(case_dir), self.session), record)
                plan, _ = make_plan([row["Case"]])
                driver.finish_group(group, plan, [row["Case"]], self.session)
                self.assertEqual(gate.sha(group / "child-0001.json"), gate.sha(child))
                self.assertTrue(gate.same_path(child, gate.filesystem_path(child)))
                if os.name == "nt":
                    self.assertEqual(gate.sha(attempt / "unused" / ".." / child.name), gate.sha(child))
                    unc = Path(r"\\server\share\folder\..\proof.json")
                    self.assertEqual(str(gate.ordinary_path(gate.filesystem_path(unc))), r"\\server\share\proof.json")
                request = gate.read(attempt / "artifact-gate-request.json")
                self.assertFalse(request["bundleRoot"].startswith("\\\\?\\"))
                driver.write_json(marker, dict(clean, cleanupVerified=False))
                with self.assertRaisesRegex(RuntimeError, "termination is unverified"):
                    driver.check_resume_ownership(results)
                binding = gate.read(attempt / "artifact-binding.json")
                proof_path = Path(binding["proofPath"])
                proof = gate.read(proof_path)
                proof["benchmarkGateCases"] = 393
                gate.write(proof_path, proof)
                binding["proofSha256"] = gate.sha(proof_path)
                gate.write(attempt / "artifact-binding.json", binding)
                with self.assertRaisesRegex(ValueError, "proof"):
                    driver.completed(row, case_dir, self.session)


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

    def extra_binding_gate(self, extra):
        def run(args, log, env, cwd):
            self.mocked_gate(args, log, env, cwd)
            request = gate.read(args[-1])
            path = (Path(request["bundleRoot"]) / extra.relative_to(self.build)
                    if extra.is_relative_to(self.build) else extra)
            proof_path = Path(request["output"]) / "artifact-proof.json"
            proof = gate.read(proof_path)
            proof["assemblies"].append({"name": "Extra.GateOnly", "fullName": "Extra.GateOnly, Version=1.0.0.0",
                                        "path": str(path), "mvid": str(uuid.uuid5(uuid.NAMESPACE_DNS, "Extra.GateOnly")),
                                        "sha256": gate.sha(path)})
            gate.write(proof_path, proof)
        return run

    def test_gate_only_bundle_dll_change_requires_fresh_proof(self):
        extra = self.binaries / "Extra.GateOnly.dll"
        extra.write_text("synthetic gate-only DLL v1")
        run = self.extra_binding_gate(extra)
        row, first = self.case(1)
        before = gate.ensure_artifact_gate(row, first, self.session, self.project, run, lambda: {})
        extra.write_text("synthetic gate-only DLL v2")
        row, second = self.case(2, "MediatRHistoricalNotification1", pid=222)
        result = gate.ensure_artifact_gate(row, second, self.session, self.project, run, lambda: {})
        self.assertFalse(result["proofReused"])
        self.assertNotEqual(before["key"], result["key"])
        self.assertEqual(len(self.gate_calls), 2)
        first_request = gate.read(first / "artifact-gate-request.json")
        second_request = gate.read(second / "artifact-gate-request.json")
        self.assertNotEqual(first_request["bundleFiles"], second_request["bundleFiles"])
        self.assertNotIn("Extra.GateOnly", [item["name"] for item in gate.read(next(second.glob("loaded-final-*.json")))["assemblies"]])

    def test_relabelled_cache_cannot_reuse_proof_for_different_full_bundle(self):
        extra = self.binaries / "Extra.GateOnly.dll"
        extra.write_text("synthetic unmeasured DLL v1")
        row, first = self.case(1)
        before = self.validate(row, first)
        cache_root = first.parents[4] / "artifact-gate-cache"
        saved = gate.read(cache_root / (before["key"] + ".json"))
        extra.write_text("synthetic unmeasured DLL v2")
        row, second = self.case(2, pid=222)
        final, _, _ = gate.child_snapshot(row, second)
        semantic = gate.semantic_identity(final, self.session, self.project, final["runtimeEvidence"], gate.tree_hashes(self.build))
        key = gate.digest(semantic)
        saved.update(semantic=semantic, key=key)
        gate.write(cache_root / (key + ".json"), saved)
        with self.assertRaisesRegex(ValueError, "full saved bundle"):
            self.validate(row, second)
        self.assertEqual(len(self.gate_calls), 1)
        self.assertFalse((second / "artifact-binding.json").exists())

    def test_cached_gate_only_framework_binding_change_is_rejected(self):
        extra = self.framework / "Extra.GateOnly.dll"
        extra.write_text("synthetic gate-only framework DLL v1")
        run = self.extra_binding_gate(extra)
        row, first = self.case(1)
        gate.ensure_artifact_gate(row, first, self.session, self.project, run, lambda: {})
        extra.write_text("synthetic gate-only framework DLL v2")
        row, second = self.case(2, pid=222)
        with self.assertRaisesRegex(ValueError, "Actual gate loaded binding changed"):
            gate.ensure_artifact_gate(row, second, self.session, self.project, run, lambda: {})
        self.assertEqual(len(self.gate_calls), 1)
        self.assertFalse((second / "artifact-binding.json").exists())


    def test_gate_implementation_version_participates_in_cache_key(self):
        row, first = self.case(1)
        before = self.validate(row, first)["key"]
        (self.project / "ArtifactGate.cs").write_text("changed gate implementation")
        row, second = self.case(2, pid=222)
        self.assertNotEqual(before, self.validate(row, second)["key"])
        self.assertEqual(len(self.gate_calls), 2)

    def test_struct_gate_implementation_participates_explicitly_in_semantic_cache_key(self):
        row, first = self.case(1)
        before = self.validate(row, first)["key"]
        (self.project / "StructUpstreamGate.cs").write_text("changed additional gate implementation")
        row, second = self.case(2, pid=222)
        self.assertNotEqual(before, self.validate(row, second)["key"])
        self.assertEqual(len(self.gate_calls), 2)

    def test_additional_proof_missing_mutated_failed_hash_count_and_classification_are_rejected(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        request_path = attempt / "artifact-gate-request.json"
        request = gate.read(request_path)
        proof_path = Path(request["output"]) / "artifact-proof.json"
        cases_path = proof_path.parent / "struct-upstream/struct-upstream-correctness.json"
        types_path = proof_path.parent / "struct-upstream/struct-upstream-driver-identification.json"
        original_proof, original_cases, original_types = gate.read(proof_path), gate.read(cases_path), gate.read(types_path)
        for mutation in ("missing-proof", "missing-results", "mutated-results", "failed-case", "wrong-hash", "wrong-count", "wrong-driver", "wrong-implementation", "duplicate-case", "duplicate-driver"):
            with self.subTest(mutation=mutation):
                proof, cases, types = copy.deepcopy(original_proof), copy.deepcopy(original_cases), copy.deepcopy(original_types)
                gate.write(cases_path, cases); gate.write(types_path, types)
                if mutation == "missing-proof": proof.pop("structUpstreamGate")
                elif mutation == "missing-results": cases_path.unlink()
                elif mutation == "mutated-results": cases[0]["Items"] = 999; gate.write(cases_path, cases)
                elif mutation == "failed-case":
                    cases[0]["status"] = "Failed"; gate.write(cases_path, cases)
                    proof["structUpstreamGate"]["correctnessSha256"] = gate.sha(cases_path)
                elif mutation == "wrong-hash": proof["structUpstreamGate"]["correctnessSha256"] = "0" * 64
                elif mutation == "wrong-count":
                    gate.write(cases_path, cases[:-1]); proof["structUpstreamGate"]["correctnessSha256"] = gate.sha(cases_path)
                elif mutation == "wrong-driver":
                    types[0]["Library"] = "Mediator"; gate.write(types_path, types)
                    proof["structUpstreamGate"]["driverIdentificationSha256"] = gate.sha(types_path)
                elif mutation == "wrong-implementation": proof["structUpstreamGate"]["implementationSha256"] = "0" * 64
                elif mutation == "duplicate-case":
                    cases[1] = copy.deepcopy(cases[0]); gate.write(cases_path, cases)
                    proof["structUpstreamGate"]["correctnessSha256"] = gate.sha(cases_path)
                elif mutation == "duplicate-driver":
                    types[1] = copy.deepcopy(types[0]); gate.write(types_path, types)
                    proof["structUpstreamGate"]["driverIdentificationSha256"] = gate.sha(types_path)
                gate.write(proof_path, proof)
                with self.assertRaises(ValueError): gate.verify_gate(request_path, proof_path)
        gate.write(cases_path, original_cases); gate.write(types_path, original_types); gate.write(proof_path, original_proof)

    def test_389_only_cache_is_not_reused_as_opt_in_proof(self):
        row, first = self.case(1)
        before = self.validate(row, first)
        cache_path = first.parents[4] / "artifact-gate-cache" / (before["key"] + ".json")
        cache = gate.read(cache_path)
        proof_path = Path(cache["proofPath"])
        proof = gate.read(proof_path); proof.pop("structUpstreamGate"); gate.write(proof_path, proof)
        cache["proofSha256"] = gate.sha(proof_path); gate.write(cache_path, cache)
        row, second = self.case(2, pid=222)
        with self.assertRaisesRegex(ValueError, "struct upstream additional proof"):
            self.validate(row, second)
        self.assertEqual(len(self.gate_calls), 1)
        self.assertFalse((second / "artifact-binding.json").exists())

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

    def test_incomplete_benchmark_gate_proof_is_rejected(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        path = Path(gate.read(attempt / "artifact-binding.json")["proofPath"])
        proof = gate.read(path)
        proof["benchmarkGateCases"] = 393
        gate.write(path, proof)
        binding_path = attempt / "artifact-binding.json"
        binding = gate.read(binding_path)
        binding["proofSha256"] = gate.sha(path)
        gate.write(binding_path, binding)
        with self.assertRaisesRegex(ValueError, "proof"):
            gate.check_artifact_binding(row, attempt, self.session, self.project)


class SupplementalArtifactGateContracts(SyntheticArtifactGateFixture):
    consumer = "ShortCircuitComparison"

    def setUp(self):
        super().setUp()
        names = ("ArtifactGate.cs", "ArtifactEvidence.cs", "ChildEvidence.cs", "BenchmarkGate.cs",
                 "MeasurementProgram.cs", "RuntimeEvidence.cs")
        for name in names:
            (self.project / name).write_text("synthetic supplemental implementation: " + name)
        self.enterContext(patch.object(gate, "implementation_files",
                                      return_value={name: self.project / name for name in names}))

    def case(self, ordinal=1, type="ZendiatorShortCircuit", pid=111, results=None):
        return super().case(ordinal, type, pid, results, method="WarmDispatch", project="short")

    def test_saved_auxiliary_consumer_proves_its_suite_and_preserves_loaded_map(self):
        row, attempt = self.case()
        result = self.validate(row, attempt)
        self.assertEqual(gate.check_artifact_binding(row, attempt, self.session, self.project), result)
        request = gate.read(attempt / "artifact-gate-request.json")
        proof = gate.read(Path(request["output"]) / "artifact-proof.json")
        self.assertEqual((request["consumerName"], request["benchmarkSuite"], request["benchmarkGateCases"]),
                         (self.consumer, "short", 6))
        self.assertEqual((proof["consumerName"], proof["benchmarkSuite"], proof["benchmarkGateCases"]),
                         (self.consumer, "short", 6))
        loaded = {item["name"]: item for item in request["expectedAssemblies"]}
        self.assertEqual(loaded.keys(), {item["name"] for item in self.loads})
        self.assertIn(self.consumer + "-1", loaded)
        self.assertTrue((gate.REQUIRED - {"Zendiator.UseCaseBenchmarks"}) | {self.consumer} <= loaded.keys())
        self.assertTrue(gate.within(request["consumerPath"], attempt / "bundle"))
        self.assertEqual(gate.sha(request["consumerPath"]), loaded[self.consumer]["sha256"])
        self.assertEqual(set(proof["semanticFiles"]), {"correctness.json", "details.json"})
        self.assertNotIn("structUpstreamGate", proof)
        self.assertEqual(self.gate_calls[0][-3:], [request["consumerPath"], "--artifact-gate",
                                                 str(attempt / "artifact-gate-request.json")])

    def test_auxiliary_request_and_proof_suite_count_or_consumer_substitution_is_rejected(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        request_path = attempt / "artifact-gate-request.json"
        original_request = gate.read(request_path)
        proof_path = Path(original_request["output"]) / "artifact-proof.json"
        original_proof = gate.read(proof_path)
        for target, key in itertools.product(("request", "proof"),
                                             ("benchmarkSuite", "benchmarkGateCases", "consumerName")):
            replacements = {"benchmarkSuite": "hooks", "benchmarkGateCases": 18, "consumerName": "HPeerComparison"}
            with self.subTest(target=target, key=key):
                request, proof = copy.deepcopy(original_request), copy.deepcopy(original_proof)
                (request if target == "request" else proof)[key] = replacements[key]
                gate.write(request_path, request)
                proof["requestHash"] = gate.sha(request_path)
                gate.write(proof_path, proof)
                with self.assertRaises(ValueError):
                    gate.verify_gate(request_path, proof_path)
        gate.write(request_path, original_request)
        gate.write(proof_path, original_proof)

    def test_auxiliary_missing_changed_or_unbound_semantic_details_are_rejected(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        request_path = attempt / "artifact-gate-request.json"
        proof_path = Path(gate.read(request_path)["output"]) / "artifact-proof.json"
        details = proof_path.parent / "details.json"
        original_proof, original_details = gate.read(proof_path), gate.read(details)
        for mutation in ("missing-file", "changed-file", "unbound-file"):
            with self.subTest(mutation=mutation):
                proof = copy.deepcopy(original_proof)
                gate.write(details, original_details)
                if mutation == "missing-file":
                    details.unlink()
                elif mutation == "changed-file":
                    gate.write(details, {"fixture": "substituted semantic work"})
                else:
                    proof["semanticFiles"].pop("details.json")
                gate.write(proof_path, proof)
                with self.assertRaisesRegex(ValueError, "semantic details"):
                    gate.verify_gate(request_path, proof_path)
        gate.write(details, original_details)
        gate.write(proof_path, original_proof)

    def test_rehashed_auxiliary_correctness_must_pass_and_cover_the_measured_method(self):
        row, attempt = self.case()
        self.validate(row, attempt)
        request_path = attempt / "artifact-gate-request.json"
        proof_path = Path(gate.read(request_path)["output"]) / "artifact-proof.json"
        outcomes = proof_path.parent / "correctness.json"
        original_proof, original_outcomes = gate.read(proof_path), gate.read(outcomes)
        for mutation in ("missing-case", "failed-case", "wrong-type", "wrong-method", "wrong-parameters"):
            with self.subTest(mutation=mutation):
                proof, rows = copy.deepcopy(original_proof), copy.deepcopy(original_outcomes)
                if mutation == "missing-case":
                    rows.pop()
                elif mutation == "failed-case":
                    rows[0]["status"] = "Failed"
                elif mutation == "wrong-type":
                    rows[0]["type"] = "Competitive.ShortCircuit.UnmeasuredEntry"
                elif mutation == "wrong-method":
                    rows[0]["method"] = "UnmeasuredMethod"
                else:
                    rows[0]["parameters"] = {"Count": 16}
                gate.write(outcomes, rows)
                proof["correctnessSha256"] = gate.sha(outcomes)
                proof["semanticFiles"]["correctness.json"] = gate.sha(outcomes)
                gate.write(proof_path, proof)
                with self.assertRaisesRegex(ValueError, "semantic gate|does not cover"):
                    gate.verify_gate(request_path, proof_path)
        gate.write(outcomes, original_outcomes)
        gate.write(proof_path, original_proof)

    def test_auxiliary_child_requires_its_consumer_and_product_di_bindings(self):
        required = (gate.REQUIRED - {"Zendiator.UseCaseBenchmarks"}) | {self.consumer}
        for ordinal, missing in enumerate(sorted(required), 1):
            with self.subTest(missing=missing):
                row, attempt = self.case(ordinal)
                setup_path = attempt / "child-setup.json"
                setup = gate.read(setup_path)
                setup["assemblies"] = [item for item in setup["assemblies"] if item["name"] != missing]
                gate.write(setup_path, setup)
                final_path = next(attempt.glob("loaded-final-*.json"))
                final = gate.read(final_path)
                final["assemblies"] = [item for item in final["assemblies"] if item["name"] != missing]
                final["initialEvidenceSha256"] = gate.sha(setup_path)
                gate.write(final_path, final)
                with self.assertRaisesRegex(ValueError, "mandatory child bindings"):
                    self.validate(row, attempt)
        self.assertFalse(self.gate_calls)

    def test_389_plus_96_proof_cannot_be_relabelled_as_a_six_case_auxiliary_suite(self):
        row, attempt = self.case()

        def legacy_proof(args, log, env, cwd):
            self.mocked_gate(args, log, env, cwd, usecase=True)
            request = gate.read(args[-1])
            proof_path = Path(request["output"]) / "artifact-proof.json"
            proof = gate.read(proof_path)
            proof.update(consumerName=self.consumer, benchmarkSuite="short")
            self.assertEqual((proof["benchmarkGateCases"], proof["structUpstreamGate"]["cases"]), (389, 96))
            gate.write(proof_path, proof)

        with self.assertRaisesRegex(ValueError, "proof is incomplete"):
            gate.ensure_artifact_gate(row, attempt, self.session, self.project, legacy_proof, lambda: {})
        self.assertFalse((attempt / "artifact-binding.json").exists())


if __name__ == "__main__":
    unittest.main()
