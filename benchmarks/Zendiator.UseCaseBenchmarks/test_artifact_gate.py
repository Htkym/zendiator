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
        for name in ("ArtifactGate.cs", "ArtifactEvidence.cs", "Program.cs", "ChildEvidence.cs", "StructUpstreamGate.cs", "perf03_counted_map.py"):
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


def make_synthetic_perf03_protocol_evidence():
    """Build validator-test data only; this is never runtime or measurement evidence."""
    result = {
        "schemaVersion": 1,
        "protocol": "zendiator-perf03-raw-protocol-v1",
        "producerSubstitution": True,
        "actualWorkPendingFractionClaim": False,
        "isolatedSnapshotExecuted": False,
        "normalTypedNativeDi": True,
        "apiCancellationContractClaim": False,
        "scope": gate._PERF03_PROTOCOL_SCOPE,
        "attribution": gate._PERF03_PROTOCOL_ATTRIBUTION,
        "rows": [],
    }
    for route in ("Stream0", "Relay5"):
        relay = route == "Relay5"
        for name in gate._PERF03_PROTOCOL_NAMES:
            cleanup, reentry, kind, tickets = gate._perf03_protocol_shape(route, name)
            count, total, pulls = gate._PERF03_PROTOCOL_COUNTS[name]
            raw_rows = []
            for index, ticket in enumerate(tickets):
                precompleted = kind.startswith("precompleted-")
                registrations = 0 if precompleted or kind == "before-registration" else 1
                registered = ticket["id"] if registrations else None
                returned_status = {"precompleted-success": "Succeeded", "precompleted-fault": "Faulted", "precompleted-cancelled": "Canceled"}.get(kind)
                raw_rows.append({
                    "kind": kind,
                    "statusChecks": 2,
                    "returnedStatuses": [returned_status, returned_status] if precompleted else ["Pending", "Succeeded"] if kind == "before-registration" else ["Pending", "Pending"],
                    "registrations": registrations,
                    "consumptions": 1,
                    "callbacks": registrations,
                    "inlineCallbacks": 0 if kind == "async-once" or precompleted else registrations,
                    "useSchedulingContext": False,
                    "ticket": dict(ticket),
                    "issuedParentTicket": tickets[0]["id"] if reentry and index == 1 else None,
                    "registrationAmbientTicket": registered,
                    "registeredTicket": registered,
                    "resultAmbientTicket": ticket["id"],
                    "resultTicket": ticket["id"],
                    "callbackEntryTicket": registered,
                    "callbackExitCapturedTicket": registered,
                    "callbackExitAmbientTicket": registered,
                })
            row = {
                "route": route,
                "scenario": name,
                "status": "Passed",
                "expectedRepeatedMovePath": "GroupPending" if relay else "CompletionCell",
                "inputCursorType": "Competitive.Perf03.Diagnostics.Perf03ProtocolCursor",
                "typedEntryType": "Competitive.Generated.Perf03ProtocolStreams.Zendiator",
                "nativeTypedEntryObserved": True,
                "handlerRequestReferenceEquals": True,
                "handlerApiTokenEquals": True,
                "handlerNativeScopeReferenceEquals": True,
                "cachedHandlerReferenceEquals": True,
                "enumerationTokenDefault": True,
                "tokenMode": "Live" if not relay or name == "completed-cancelled" else "Default",
                "apiTokenCanBeCanceled": not relay or name == "completed-cancelled",
                "apiTokenCancellationRequested": False,
                "sourceCancellationIndependent": name == "completed-cancelled",
                "apiCancellationContractClaim": False,
                "outcome": "OperationCanceledException" if name == "completed-cancelled" else "ArithmeticException" if name in ("repeated-pending-failure", "pending-cleanup-failure", "completed-fault") else "Completed",
                "values": list(range(count)),
                "count": count,
                "sum": total,
                "pulls": pulls,
                "currentReads": count,
                "disposals": 1,
                "events": [1, 2, 3, 4, 5, 100] if relay else [100],
                "pendingAtReturn": [True, True] if reentry else [False] * len(tickets) if kind.startswith("precompleted-") else [False] if name in ("before-registration", "inside-registration") else [True],
                "reenteredDuringCompletion": reentry,
                "oldCallbackExitTicket": tickets[0]["id"] if reentry else None,
                "newOperationIssuedParentTicket": tickets[0]["id"] if reentry else None,
                "rawMoves": [] if cleanup else raw_rows,
                "rawCleanup": raw_rows[0] if cleanup else None,
            }
            result["rows"].append(row)
    return result

class Perf03ArtifactGateContracts(SyntheticArtifactGateFixture):
    def setUp(self):
        super().setUp()
        for name in ("Perf03CauseGate.cs", "Perf03CauseStreams.cs"):
            (self.project / name).write_text("synthetic PERF03 gate fingerprint")
        self.diagnostics = self.project.parent.parent / "src/Perf03CauseDiagnostics"
        # Copy hash-bound source fixtures; the synthetic consumer is never executed.
        source_diagnostics = Path(__file__).resolve().parents[2] / "src/Perf03CauseDiagnostics"
        shutil.copytree(source_diagnostics, self.diagnostics)
        self.original_control = next(name for name in gate.PERF03_DIAGNOSTIC_FILES if name.endswith(".g.cs.txt"))

    def perf03_case(self, ordinal=1, pid=111, case=None):
        case = case or gate.perf03_case_set()[0]
        row, attempt = self.case(ordinal, type=case["Type"], method=case["Method"], pid=pid)
        row["Case"] = dict(case)
        return row, attempt

    @staticmethod
    def synthetic_contracts(relay):
        names = ["cold-disposal", "early-disposal", "producer-failure", "pre-cancelled-api", "cancel-after-first",
                 "distinct-enumeration-token", "current-lifecycle", "ambient-context"] + (["relay-failure"] if relay else [])
        rows = []
        for name in names:
            started = name not in {"cold-disposal", "pre-cancelled-api", "relay-failure"}
            outcome = ("ArithmeticException" if name in {"producer-failure", "relay-failure"} else
                       "OperationCanceledException" if name in {"pre-cancelled-api", "cancel-after-first", "distinct-enumeration-token"} else "Completed")
            items = (1 if name in {"early-disposal", "ambient-context", "cancel-after-first", "distinct-enumeration-token"} else
                     1024 if name == "current-lifecycle" else 0)
            observed = dict(outcome=outcome, items=items, Handlers=int(started), Disposals=int(started),
                            events=([1, 2, 3, 4, 5, 100] if relay else [100]) if started else [])
            rows.append(dict(name=name, status="Passed", normal=observed, isolated=copy.deepcopy(observed)))
        return rows

    @staticmethod
    def synthetic_raw_work(relay, controlled, mapped):
        """Construct test-only traces, including one old-ticket retirement after a new pull."""
        filename = "Counted.Relay5.g.cs" if relay else "Counted.Stream0.g.cs"
        cursor = 5 if relay else 0
        def find(kind=None, method=None, line=None, file=None):
            return next(site for site in mapped if (kind is None or site["kind"] == kind) and
                (method is None or site["method"] == method) and (line is None or site["originLine"] == line) and
                site["file"] == (file or filename) and (file is not None or site["declaringCursor"] == cursor or kind == "input.raw.pull"))
        claim = find(line=88, file="CountedStreamCursorState.cs")
        pull = find(kind="input.raw.pull")
        implicit_begin = find(kind="input.raw.await.implicit.begin", method="StartMoveAsync")
        implicit_consume = find(kind="input.raw.consume.implicit", method="StartMoveAsync")
        initial = find(kind="input.raw.IsCompletedSuccessfully", method="AdvanceIdentityLease" if relay else "MoveNextAsync")
        direct = find(kind="input.raw.consume.explicit", method="AdvanceIdentityLease" if relay else "MoveNextAsync")
        recheck = find(kind="input.raw.awaiter.IsCompleted", method="Start")
        resumed = find(kind="input.raw.consume.explicit", method="ResumeCore")
        begin = find(kind="runner.BeginGroup" if relay else "runner.Begin")
        register = find(kind="raw.register")
        prepare = find(line=533, file="CountedStreamCursorState.cs")
        published = find(line=568, file="CountedStreamCursorState.cs")
        consumed = find(line=589, file="CountedStreamCursorState.cs")
        retired = find(line=574, file="CountedStreamCursorState.cs")
        completion_result = find(kind="completion.consume.explicit", file="CountedStreamCursorState.cs")
        origin = next(site["originFile"] for site in mapped if site["file"] == filename)
        root_type = "Competitive.Perf03.Counted.StreamSource_" + origin.split(".")[2] + "+Cursor" + str(cursor)
        owners = {"1": root_type}
        if controlled:
            owners.update({"2": "Competitive.Perf03.Counted.CountedStreamCursorState+MoveCompletion",
                           "3": "Competitive.Perf03.Counted.CountedStreamCursorState+MoveCompletion"})
        events, held = [], []
        reentry = dict(inline=True, oldOperation=1, newOperation=2, oldOwner=2,
                       oldRetirementSite=retired["id"], oldRetirementPhase="repeated")
        def emit(site, operation, observed=None, owner=None):
            phase = "first" if operation == 0 else "repeated" if operation < 1024 else "eof"
            events.append(dict(Sequence=len(events) + 1, Operation=operation, Phase=phase,
                Site=site["id"], Kind=site["kind"], Observed=observed, Owner=owner))
            return len(events)
        for operation in range(1025):
            emit(claim, operation, owner=1)
            if operation == 0:
                emit(implicit_begin, operation)
            pull_sequence = emit(pull, operation)
            if operation == 0:
                emit(implicit_consume, operation)
            elif controlled and operation < 1024:
                owner = 3 if operation == 2 else 2
                emit(initial, operation, False)
                emit(recheck, operation, False)
                emit(begin, operation)
                emit(prepare, operation, owner=owner)
                emit(register, operation)
                if operation == 2:
                    reentry["newRawPullSequence"] = pull_sequence
                    reentry["oldRetirementSequence"] = emit(retired, 1, owner=2)
                emit(resumed, operation, True)
                publication_sequence = emit(published, operation, owner=owner)
                emit(completion_result, operation, True)
                consumption_sequence = emit(consumed, operation, owner=owner)
                if operation == 1:
                    reentry["oldPublicationSequence"] = publication_sequence
                    reentry["oldConsumptionSequence"] = consumption_sequence
                else:
                    emit(retired, operation, owner=owner)
            else:
                emit(initial, operation, True)
                emit(direct, operation, operation < 1024)
            if controlled and operation < 1024:
                held.append(dict(operation=operation, pendingAtReturn=True, queuedCallbacks=1, rawPulls=1,
                    rawConsumptionsBeforePump=0, rawRegistrations=0 if operation == 0 else 1,
                    implicitCompilerAwaitBegins=1 if operation == 0 else 0, incompleteAwaiterRechecks=0 if operation == 0 else 1))
        phases = []
        for phase in ("acquisition", "first", "repeated", "eof", "cleanup"):
            current = [event for event in events if event["Phase"] == phase]
            phases.append(dict(phase=phase, rawPulls=sum(e["Kind"] == "input.raw.pull" for e in current),
                explicitRawConsumptions=sum(e["Kind"] == "input.raw.consume.explicit" for e in current),
                implicitCompilerAwaitConsumptions=sum(e["Kind"] == "input.raw.consume.implicit" for e in current)))
        grouped = {}
        for event in events:
            key = (event["Phase"], event["Site"], event["Kind"])
            values = grouped.setdefault(key, [0, 0, 0]); values[0] += 1
            values[1] += event["Observed"] is True; values[2] += event["Observed"] is False
        site_counts = [dict(phase=key[0], site=key[1], kind=key[2], calls=value[0], trueObservations=value[1], falseObservations=value[2])
                       for key, value in grouped.items()]
        return dict(case=dict(Type="ZendiatorPerf03NamedDefaultRelay5" if relay else "ZendiatorPerf03NamedLiveStream0",
                Method="FullDiagnostic", Count=1024, Asynchronous=True), status="Passed", controlledScheduling=controlled,
            countedTraceAttributionValidated=controlled, controlledYield=dict(postedCallbacks=1024, executedCallbacks=1024,
                heldItemOperations=held, synchronousEof=True, reentry=reentry) if controlled else None,
            producer="WorkStreamCursor", producerSubstitution=False, typedNativeDi=True, items=1024, sum=523776,
            handlers=1, disposals=1, callbackOrder=[1, 2, 3, 4, 5, 100] if relay else [100], rawPulls=1025,
            rawConsumptions=1025, rawConsumptionPerOperationVerified=True, firstMoveCompilerAwaitInstrumentedAtSourceBoundary=True,
            compilerGeneratedGetResultInstructionCountClaim=False, consumerObserved=dict(moveCalls=1025, consumptions=1025,
                incompleteAtReturn=1024 if controlled else 0), phaseCounts=phases, completedBetweenChecks=[], siteCounts=site_counts,
            ownerTypes=owners, trace=events, countedCopyPerturbsScheduling=True, timedPendingFractionKnown=False)

    def synthetic_raw_preflight(self, request, proof):
        prefix = "perf03-sources/src/Perf03CauseDiagnostics/"
        site_map = Path(request["bundleRoot"]) / (prefix + "counted-sites.json")
        mapped = gate.read(site_map)["sites"]
        metadata = dict(schemaVersion=1, protocol="zendiator-perf03-raw-preflight-v1",
            evidencePath="perf03-cause-raw/raw-preflight.json", siteMapPath=prefix + "counted-sites.json",
            siteMapSha256=gate.sha(site_map), compiledSiteCount=len(mapped))
        raw = dict(schemaVersion=1, protocol=metadata["protocol"], implementationSha256=request["perf03CauseGateImplementationSha256"],
            variant="V0", siteMapSha256=metadata["siteMapSha256"], compiledSiteCount=len(mapped), unmappedSiteRejected=True,
            mismatchedKindRejected=True, countedCopyPerturbsScheduling=True, timedPendingFractionKnown=False, nsPerPendingClaim=False,
            counterSynchronizationExcludedFromAtomicCounts=True, deterministicProtocolsValidateCountedTraceAttribution=False,
            controlledWorkValidatesCountedTraceAttribution=True, countedWorkFaultOrCancelConsumptionClaim=False,
            countedWorkConsumptionScope="Successful Work results only; throwing GetResult attempts are not counted by the copied-route success markers",
            actualWorkCases=[self.synthetic_raw_work(relay, controlled, mapped) for relay in (False, True) for controlled in (False, True)],
            deterministicProtocols=make_synthetic_perf03_protocol_evidence())
        path = Path(request["output"]) / metadata["evidencePath"]
        gate.write(path, raw)
        metadata["evidenceSha256"] = gate.sha(path)
        proof["perf03CauseGate"]["rawPreflight"] = metadata

    def mocked_perf03_gate(self, args, log, env, cwd):
        self.mocked_gate(args, log, env, cwd)
        request = gate.read(args[-1])
        proof_path = Path(request["output"]) / "artifact-proof.json"
        proof = gate.read(proof_path)
        rows = []
        for case in request["perf03CauseCaseSet"]:
            direct = case["Method"] == "FullDirect"
            relay = case["Type"].endswith("Relay5")
            rows.append(dict(case=case, status="Passed", count=1024, sum=523776,
                             handlers=1, items=1024, disposals=1,
                             producerRepresentation="Iterator" if "Iterator" in case["Type"] else "Named",
                             tokenMode="Live" if "Live" in case["Type"] else "Default", relayDepth=5 if relay else 0,
                             events=[1, 2, 3, 4, 5, 100] if relay and not direct else [100],
                             enumerationTokenDefault=True, publicEnumerationTokenApplicable=not direct, sourceTokenMatchesApi=True, nativeDiHandlerObserved=not direct,
                             identities=dict(requestReferenceObserved=not direct, requestIdentityApplicable=not direct, sourceProbeReferenceObserved=True,
                                 nativeEntryReferenceObserved=not direct, nativeScopeReferenceObserved=not direct,
                                 cachedHandlerType=None if direct else "Synthetic.CachedHandler", cachedHandlerCount=0 if direct else 1),
                             consumerPreflight=dict(moveCalls=1025, consumptions=1025, incompleteAtReturn=0,
                                 firstMoveIncomplete=False, repeatedItemIncomplete=0, eofIncomplete=False,
                                 rawDecisionSitesObserved=False, timingPendingFractionKnown=False),
                             normalParity=True, sequentialEnvelopeParity=True, sharedEntryAndCachedHandler=True, sharedRequest=True,
                             selectedVariantProtocols=dict(status="Passed", uninstrumentedSelectedCandidate=True,
                                 producer="WorkStreamCursor", producerSubstitution=False, controlledScheduling=True,
                                 pendingDisposeRejected=True, inlineConsumptionAndNextPendingMove=True,
                                 disposalInsideContinuation=True, oldUnconsumedValueTaskInvalidationChecked=False,
                                 durablePrecreatedTaskChecked=False, timedPendingFractionClaim=False),
                             contractChecks=self.synthetic_contracts(relay) if case["Method"] == "FullDiagnostic" else None))
        evidence = dict(schemaVersion=1, protocol=gate.PERF03_PROTOCOL,
                        variant=request["perf03CauseVariant"], implementationSha256=request["perf03CauseGateImplementationSha256"],
                        caseSet=request["perf03CauseCaseSet"], pendingFractionClaim=False,
                        variantMetadata=request["perf03CauseVariantMetadata"],
                        variantEnvelope=dict(sequential=True, oneEnumeratorPerOperation=True, awaitAndConsumeBeforeNextMove=True,
                            savedUnconsumedValueTask=False, concurrentMoveOrDispose=False, failureAndCleanupChecked=True,
                            productionGuaranteesClaimed=False, ambientContext="Consumer AsyncLocal preservation only; producer context unprobed",
                            unsupportedProbes=["saved-unconsumed-value-task", "concurrent-move-dispose", "raw-pending-decision-site-counts"]),
                        normalInputCases=[row for row in rows if row["case"]["Method"] != "FullDiagnostic"],
                        isolatedControls=[row for row in rows if row["case"]["Method"] == "FullDiagnostic"])
        evidence_path = proof_path.parent / "perf03-cause/perf03-cause.json"
        gate.write(evidence_path, evidence)
        proof.update({name: request[name] for name in gate.PERF03_FIELDS})
        proof["perf03CauseGate"] = dict(schemaVersion=1, protocol=gate.PERF03_PROTOCOL,
                                        variant=evidence["variant"], implementationSha256=evidence["implementationSha256"],
                                        caseSet=evidence["caseSet"], evidencePath="perf03-cause/perf03-cause.json",
                                        evidenceSha256=gate.sha(evidence_path))
        self.synthetic_raw_preflight(request, proof)
        gate.write(proof_path, proof)

    def validate_perf03(self, row, attempt):
        return gate.ensure_artifact_gate(row, attempt, self.session, self.project, self.mocked_perf03_gate, lambda: {})

    def test_fixed_26_case_set_rejects_unmeasured_parameters_and_routes(self):
        cases = gate.perf03_case_set()
        self.assertEqual((len(cases), len({gate.canonical(case) for case in cases})), (26, 26))
        self.assertEqual(sum(case["Method"] == "FullDirect" for case in cases), 8)
        self.assertEqual(sum(case["Method"] == "FullDiagnostic" for case in cases), 2)
        for mutation in (dict(Count=True), dict(Asynchronous=1), dict(Lifetime="Default"),
                         dict(Type="ZendiatorPerf03NamedDefaultRelay1"), dict(Method="EarlyBreak"), dict(Project="short")):
            with self.subTest(mutation=mutation), self.assertRaisesRegex(ValueError, "PERF03 case"):
                gate.perf03_selected(dict(cases[0], **mutation))

    def test_selected_case_cannot_accept_only_the_original_389_plus_96_proof(self):
        row, attempt = self.perf03_case()
        with self.assertRaisesRegex(ValueError, "PERF03 diagnostic implementation"):
            self.validate(row, attempt)
        self.assertFalse((attempt / "artifact-binding.json").exists())

    def test_sources_are_archived_without_mutating_build_and_scope_remains_separate(self):
        original = gate.tree_hashes(self.build)
        row, attempt = self.perf03_case()
        result = self.validate_perf03(row, attempt)
        self.assertEqual(gate.tree_hashes(self.build), original)
        request = gate.read(attempt / "artifact-gate-request.json")
        sources = request["perf03CauseSourceFiles"]
        self.assertIn("perf03-sources/src/Perf03CauseDiagnostics/" + self.original_control, sources)
        self.assertEqual(request["perf03CauseGateImplementationSha256"], gate.digest(sources))
        self.assertTrue(all(request["bundleFiles"][name] == value for name, value in sources.items()))
        self.assertIn("389 existing benchmark gate plus separate 96", result["scope"])
        self.assertIn("Separate 24 PERF03", result["perf03CauseScope"])
        self.assertIn("four ordinary/controlled counted Work", result["perf03RawScope"])
        self.assertEqual(gate.check_artifact_binding(row, attempt, self.session, self.project), result)

    def test_changed_source_or_variant_prevents_resume(self):
        row, attempt = self.perf03_case()
        self.validate_perf03(row, attempt)
        for name in ("Perf03DiagnosticControl.cs", "Perf03CauseVariant.cs", "snapshot-provenance.json"):
            with self.subTest(name=name):
                path = self.diagnostics / name
                original = path.read_text()
                path.write_text(original + "\n// Changed source identity")
                with self.assertRaisesRegex(ValueError, "implementation"):
                    gate.check_artifact_binding(row, attempt, self.session, self.project)
                path.write_text(original)

    def test_cache_reuses_only_the_same_full_diagnostic_source_map(self):
        first_row, first = self.perf03_case()
        self.validate_perf03(first_row, first)
        second_row, second = self.perf03_case(2, pid=222, case=gate.perf03_case_set()[1])
        self.assertTrue(self.validate_perf03(second_row, second)["proofReused"])
        (self.diagnostics / self.original_control).write_text("changed retained control")
        third_row, third = self.perf03_case(3, pid=333, case=gate.perf03_case_set()[2])
        self.assertFalse(self.validate_perf03(third_row, third)["proofReused"])
        self.assertEqual(len(self.gate_calls), 2)

    def test_missing_or_substituted_diagnostic_proof_is_rejected(self):
        row, attempt = self.perf03_case()
        self.validate_perf03(row, attempt)
        request = attempt / "artifact-gate-request.json"
        proof_path = Path(gate.read(request)["output"]) / "artifact-proof.json"
        original = gate.read(proof_path)
        for name in (*gate.PERF03_FIELDS, "perf03CauseGate"):
            with self.subTest(name=name):
                proof = copy.deepcopy(original)
                proof.pop(name)
                gate.write(proof_path, proof)
                with self.assertRaisesRegex(ValueError, "PERF03"):
                    gate.verify_gate(request, proof_path)
        gate.write(proof_path, original)

    def test_rehashed_diagnostic_evidence_requires_exact_cases_and_declared_limits(self):
        row, attempt = self.perf03_case()
        self.validate_perf03(row, attempt)
        request = attempt / "artifact-gate-request.json"
        proof_path = Path(gate.read(request)["output"]) / "artifact-proof.json"
        evidence_path = proof_path.parent / "perf03-cause/perf03-cause.json"
        original_proof, original = gate.read(proof_path), gate.read(evidence_path)
        for mutation in ("missing-case", "duplicate-case", "failed-case", "wrong-sum", "boolean-count", "token", "cleanup", "order", "producer", "native-scope", "raw-pending-claim", "consumer-count", "pending-count", "control-parity", "shared-request", "enumeration-applicability", "ambient-scope", "pending-claim", "nonsequential", "variant"):
            with self.subTest(mutation=mutation):
                proof, evidence = copy.deepcopy(original_proof), copy.deepcopy(original)
                if mutation == "missing-case":
                    evidence["normalInputCases"].pop()
                elif mutation == "duplicate-case":
                    evidence["normalInputCases"][0] = copy.deepcopy(evidence["normalInputCases"][1])
                elif mutation == "failed-case":
                    evidence["isolatedControls"][0]["status"] = "Failed"
                elif mutation == "wrong-sum":
                    evidence["normalInputCases"][0]["sum"] = 0
                elif mutation == "boolean-count":
                    evidence["normalInputCases"][0]["count"] = True
                elif mutation == "token":
                    evidence["normalInputCases"][0]["sourceTokenMatchesApi"] = False
                elif mutation == "cleanup":
                    evidence["normalInputCases"][0]["disposals"] = 2
                elif mutation == "order":
                    evidence["isolatedControls"][0]["events"] = [100, 1, 2, 3, 4, 5]
                elif mutation == "producer":
                    evidence["normalInputCases"][0]["producerRepresentation"] = "SubstitutedProducer"
                elif mutation == "native-scope":
                    evidence["isolatedControls"][0]["identities"]["nativeScopeReferenceObserved"] = False
                elif mutation == "raw-pending-claim":
                    evidence["normalInputCases"][0]["consumerPreflight"]["rawDecisionSitesObserved"] = True
                elif mutation == "consumer-count":
                    evidence["normalInputCases"][0]["consumerPreflight"]["consumptions"] = 1024
                elif mutation == "pending-count":
                    evidence["normalInputCases"][0]["consumerPreflight"]["firstMoveIncomplete"] = True
                elif mutation == "control-parity":
                    evidence["isolatedControls"][0]["normalParity"] = False
                elif mutation == "shared-request":
                    evidence["isolatedControls"][0]["sharedRequest"] = False
                elif mutation == "enumeration-applicability":
                    evidence["normalInputCases"][0]["publicEnumerationTokenApplicable"] = False
                elif mutation == "ambient-scope":
                    evidence["variantEnvelope"]["ambientContext"] = "Producer context preservation proven"
                elif mutation == "pending-claim":
                    evidence["pendingFractionClaim"] = True
                elif mutation == "nonsequential":
                    evidence["variantEnvelope"]["sequential"] = False
                else:
                    evidence["variant"] = "VRoot"
                gate.write(evidence_path, evidence)
                proof["perf03CauseGate"]["evidenceSha256"] = gate.sha(evidence_path)
                gate.write(proof_path, proof)
                with self.assertRaisesRegex(ValueError, "PERF03"):
                    gate.verify_gate(request, proof_path)
        gate.write(evidence_path, original)
        gate.write(proof_path, original_proof)

    def test_rehashed_contract_checks_require_all_observed_contracts_and_expected_outcomes(self):
        row, attempt = self.perf03_case()
        self.validate_perf03(row, attempt)
        request = attempt / "artifact-gate-request.json"
        proof_path = Path(gate.read(request)["output"]) / "artifact-proof.json"
        evidence_path = proof_path.parent / "perf03-cause/perf03-cause.json"
        original_proof, original = gate.read(proof_path), gate.read(evidence_path)
        self.assertEqual(sum(len(control["contractChecks"]) for control in original["isolatedControls"]), 17)
        for mutation in ("missing-section", "missing-contract", "duplicate-contract", "wrong-name", "failed-contract",
                         "mismatched-observations", "wrong-outcome", "wrong-items", "wrong-startup", "wrong-cleanup", "wrong-order"):
            with self.subTest(mutation=mutation):
                proof, evidence = copy.deepcopy(original_proof), copy.deepcopy(original)
                control = evidence["isolatedControls"][0]
                contracts = control["contractChecks"]
                if mutation == "missing-section":
                    control.pop("contractChecks")
                elif mutation == "missing-contract":
                    contracts.pop()
                elif mutation == "duplicate-contract":
                    contracts[0] = copy.deepcopy(contracts[1])
                elif mutation == "wrong-name":
                    contracts[0]["name"] = "unobserved-contract"
                elif mutation == "failed-contract":
                    contracts[0]["status"] = "Failed"
                else:
                    contract = next(item for item in contracts if item["name"] == "producer-failure")
                    if mutation == "mismatched-observations":
                        contract["isolated"]["items"] = 1
                    else:
                        key, value = {"wrong-outcome": ("outcome", "Completed"), "wrong-items": ("items", 1),
                                      "wrong-startup": ("Handlers", 0), "wrong-cleanup": ("Disposals", 0),
                                      "wrong-order": ("events", [100, 5, 4, 3, 2, 1])}[mutation]
                        for lane in ("normal", "isolated"):
                            contract[lane][key] = value
                gate.write(evidence_path, evidence)
                proof["perf03CauseGate"]["evidenceSha256"] = gate.sha(evidence_path)
                gate.write(proof_path, proof)
                with self.assertRaisesRegex(ValueError, "PERF03 V0 contract|declared contract"):
                    gate.verify_gate(request, proof_path)
        gate.write(evidence_path, original)
        gate.write(proof_path, original_proof)

    def test_missing_or_rehashed_raw_proof_cannot_substitute_trace_or_protocol_claims(self):
        row, attempt = self.perf03_case()
        self.validate_perf03(row, attempt)
        request = attempt / "artifact-gate-request.json"
        proof_path = Path(gate.read(request)["output"]) / "artifact-proof.json"
        raw_path = proof_path.parent / "perf03-cause-raw/raw-preflight.json"
        original_proof, original_raw = gate.read(proof_path), gate.read(raw_path)
        self.assertEqual((len(original_raw["actualWorkCases"]), len(original_raw["deterministicProtocols"]["rows"])), (4, 24))
        for mutation in ("missing-proof", "missing-file", "stale-hash", "map-hash", "site-count", "escaped-path", "duplicate-work",
                         "throwing-claim", "timed-claim", "protocol-attribution", "raw-count", "raw-operation", "unknown-site", "wrong-kind",
                         "old-ticket", "phase-total", "site-total", "between-claim", "missing-held", "reentry-order",
                         "missing-protocol", "protocol-status", "protocol-ticket", "producer-scope", "api-cancel"):
            with self.subTest(mutation=mutation):
                proof, raw = copy.deepcopy(original_proof), copy.deepcopy(original_raw)
                metadata = proof["perf03CauseGate"]["rawPreflight"]
                ordinary, controlled = raw["actualWorkCases"][:2]
                if mutation == "missing-proof":
                    proof["perf03CauseGate"].pop("rawPreflight")
                elif mutation == "stale-hash":
                    metadata["evidenceSha256"] = "A" * 64
                elif mutation == "map-hash":
                    metadata["siteMapSha256"] = "A" * 64
                elif mutation == "site-count":
                    metadata["compiledSiteCount"] += 1
                elif mutation == "escaped-path":
                    metadata["evidencePath"] = "../raw-preflight.json"
                elif mutation == "duplicate-work":
                    raw["actualWorkCases"][-1] = copy.deepcopy(ordinary)
                elif mutation == "throwing-claim":
                    raw["countedWorkFaultOrCancelConsumptionClaim"] = True
                elif mutation == "timed-claim":
                    raw["timedPendingFractionKnown"] = True
                elif mutation == "protocol-attribution":
                    raw["deterministicProtocolsValidateCountedTraceAttribution"] = True
                elif mutation == "raw-count":
                    ordinary["trace"].remove(next(e for e in ordinary["trace"] if e["Kind"] == "input.raw.pull"))
                    for sequence, event in enumerate(ordinary["trace"], 1):
                        event["Sequence"] = sequence
                elif mutation == "raw-operation":
                    event = next(e for e in ordinary["trace"] if e["Kind"] == "input.raw.pull")
                    event.update(Operation=1, Phase="repeated")
                elif mutation == "unknown-site":
                    ordinary["trace"][0]["Site"] = "unmapped-source-site"
                elif mutation == "wrong-kind":
                    ordinary["trace"][0]["Kind"] = "input.raw.pull"
                elif mutation == "old-ticket":
                    event = next(e for e in controlled["trace"] if e["Site"] == "CS.L0574.atomic" and e["Operation"] == 1)
                    event["Operation"] = 2
                elif mutation == "phase-total":
                    ordinary["phaseCounts"][2]["rawPulls"] += 1
                elif mutation == "site-total":
                    ordinary["siteCounts"][0]["calls"] += 1
                elif mutation == "between-claim":
                    ordinary["completedBetweenChecks"] = [dict(operation=1, initialSite="invented", recheckSite="invented", observation="unobserved")]
                elif mutation == "missing-held":
                    controlled["controlledYield"]["heldItemOperations"].pop()
                elif mutation == "reentry-order":
                    reentry = controlled["controlledYield"]["reentry"]
                    reentry["oldRetirementSequence"] = reentry["newRawPullSequence"]
                elif mutation == "missing-protocol":
                    raw["deterministicProtocols"]["rows"].pop()
                elif mutation == "protocol-status":
                    protocol = next(r for r in raw["deterministicProtocols"]["rows"] if r["scenario"] == "synchronous-raw")
                    protocol["rawMoves"][0]["returnedStatuses"] = ["Faulted"] * protocol["rawMoves"][0]["statusChecks"]
                elif mutation == "protocol-ticket":
                    protocol = next(r for r in raw["deterministicProtocols"]["rows"] if r["scenario"] == "consumption-reentry")
                    protocol["rawMoves"][1]["issuedParentTicket"] = protocol["rawMoves"][1]["ticket"]["id"]
                elif mutation == "producer-scope":
                    raw["deterministicProtocols"]["isolatedSnapshotExecuted"] = True
                elif mutation == "api-cancel":
                    protocol = next(r for r in raw["deterministicProtocols"]["rows"] if r["scenario"] == "completed-cancelled")
                    protocol["apiTokenCancellationRequested"] = True
                gate.write(raw_path, raw)
                if mutation == "missing-file":
                    raw_path.unlink()
                elif mutation != "stale-hash":
                    metadata["evidenceSha256"] = gate.sha(raw_path)
                gate.write(proof_path, proof)
                with self.assertRaisesRegex(ValueError, "PERF03 raw preflight|PERF03 protocol proof"):
                    gate.verify_gate(request, proof_path)
        gate.write(raw_path, original_raw)
        gate.write(proof_path, original_proof)

    def test_changed_archived_snapshot_is_rejected(self):
        row, attempt = self.perf03_case()
        self.validate_perf03(row, attempt)
        request = attempt / "artifact-gate-request.json"
        proof_path = Path(gate.read(request)["output"]) / "artifact-proof.json"
        path = attempt / "bundle/perf03-sources/src/Perf03CauseDiagnostics" / self.original_control
        path.write_text("substituted snapshot")
        with self.assertRaisesRegex(ValueError, "bundle"):
            gate.verify_gate(request, proof_path)

    def test_missing_original_provenance_is_rejected_before_gate_launch(self):
        row, attempt = self.perf03_case()
        (self.diagnostics / "snapshot-provenance.json").unlink()
        with self.assertRaisesRegex(ValueError, "snapshot/provenance sources"):
            self.validate_perf03(row, attempt)
        self.assertFalse(self.gate_calls)

    def test_changed_candidates_cannot_relabel_v0_counted_evidence(self):
        row, attempt = self.perf03_case()
        for variant in ("VEC", "VEntry", "VRoot", "VWord"):
            with self.subTest(variant=variant):
                (self.diagnostics / "Perf03CauseVariant.cs").write_text(
                    'internal const string Id = "' + variant + '";')
                with self.assertRaisesRegex(ValueError, "candidate-specific counted|candidate counted binding"):
                    self.validate_perf03(row, attempt)
        self.assertFalse(self.gate_calls)

    def test_unknown_variant_is_not_a_candidate(self):
        row, attempt = self.perf03_case()
        (self.diagnostics / "Perf03CauseVariant.cs").write_text('internal const string Id = "VNext";')
        with self.assertRaisesRegex(ValueError, "Unknown or ambiguous"):
            self.validate_perf03(row, attempt)
        self.assertFalse(self.gate_calls)

    def test_variant_metadata_is_bound_not_just_copied_between_proofs(self):
        row, attempt = self.perf03_case()
        self.validate_perf03(row, attempt)
        request_path = attempt / "artifact-gate-request.json"
        request = gate.read(request_path)
        proof_path = Path(request["output"]) / "artifact-proof.json"
        proof = gate.read(proof_path)
        metadata = dict(request["perf03CauseVariantMetadata"], countedEvidenceVariant="VWord")
        request["perf03CauseVariantMetadata"] = metadata
        proof["perf03CauseVariantMetadata"] = metadata
        gate.write(request_path, request)
        proof["requestHash"] = gate.sha(request_path)
        gate.write(proof_path, proof)
        with self.assertRaisesRegex(ValueError, "variant metadata"):
            gate.verify_gate(request_path, proof_path)


class CandidateSourcePatchContractTests(unittest.TestCase):
    def test_exact_final_newline_and_context_are_bound(self):
        from perf03_counted_map import apply_source_patch
        patch = "--- a/A.cs\n+++ b/B.cs\n@@ -1 +1 @@\n-old\n\\ No newline at end of file\n+new\n"
        result, _ = apply_source_patch(patch, {"A.cs": "old"})
        self.assertEqual(result, {"B.cs": "new\n"})
        with self.assertRaisesRegex(ValueError, "context/source mismatch"):
            apply_source_patch(patch, {"A.cs": "old\n"})

    def test_patch_cannot_retarget_matching_basename_outside_diagnostic_sources(self):
        from perf03_counted_map import apply_source_patch
        patch = "--- a/elsewhere/A.cs\n+++ b/B.cs\n@@ -1 +1 @@\n-old\n+new\n"
        with self.assertRaisesRegex(ValueError, "outside diagnostic sources"):
            apply_source_patch(patch, {"A.cs": "old\n"})


if __name__ == "__main__":
    unittest.main()
