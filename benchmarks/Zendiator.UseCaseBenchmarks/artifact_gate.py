"""Preserve actual child files and bind a separate consumer-entry gate to them.

No workspace fallback, rebuild, partial setup-map fallback or cross-session cache.
The complete managed map includes the generated BDN host in the conservative key.
"""

import hashlib
import json
import os
import re
import shutil
from pathlib import Path

from owned_process import validate_process_identity


PROTOCOL = "zendiator-child-artifact-gate-v1"
HASH = re.compile(r"^[0-9A-F]{64}$")
REQUIRED = {"Zendiator.UseCaseBenchmarks", "Zendiator", "Zendiator.Abstractions",
            "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions",
            "System.Private.CoreLib"}
COMPETITORS = {"MediatRHistorical": {"MediatR", "MediatR.Contracts"}, "Mediator": {"Mediator"},
               "DispatchR": {"DispatchR", "DispatchR.Abstractions"}, "Immediate": {"Immediate.Handlers.Shared"},
               "Zendiator": set()}
CONTROLS = {"DOTNET_gcServer", "COMPlus_gcServer", "DOTNET_gcConcurrent", "COMPlus_gcConcurrent",
            "DOTNET_GCHeapCount", "COMPlus_GCHeapCount", "DOTNET_ReadyToRun", "COMPlus_ReadyToRun",
            "DOTNET_TieredCompilation", "COMPlus_TieredCompilation", "DOTNET_TieredPGO", "COMPlus_TieredPGO",
            "DOTNET_TC_QuickJitForLoops", "COMPlus_TC_QuickJitForLoops", "DOTNET_EnableDiagnostics",
            "DOTNET_GCStress", "COMPlus_GCStress", "DOTNET_ADDITIONAL_DEPS", "DOTNET_SHARED_STORE", "DOTNET_STARTUP_HOOKS",
            "DOTNET_PROCESSOR_COUNT"}


def filesystem_path(path):
    """Use Windows extended paths only for I/O; retain ordinary paths in evidence."""
    path = Path(path)
    if os.name != "nt":
        return path
    value = str(path.absolute())
    if value.startswith("\\\\?\\"):
        return path
    if value.startswith("\\\\"):
        return Path("\\\\?\\UNC\\" + value[2:])
    return Path("\\\\?\\" + value)


def read(path):
    return json.loads(filesystem_path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(filesystem_path(path).read_bytes()).hexdigest().upper()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def digest(value):
    return hashlib.sha256(canonical(value).encode("utf-8")).hexdigest().upper()


def write(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def same_path(left, right):
    return os.path.normcase(str(Path(left).resolve())) == os.path.normcase(str(Path(right).resolve()))


def within(file, directory):
    file, directory = Path(file).resolve(), Path(directory).resolve()
    try:
        file.relative_to(directory)
        return True
    except ValueError:
        return False


def tree_hashes(root):
    root = filesystem_path(root)
    if root.is_symlink():
        raise ValueError("A saved artifact root cannot be a symlink")
    files = {}
    for path in sorted(root.rglob("*")):
        if path.is_symlink() or (os.name == "nt" and path.is_junction()):
            raise ValueError(f"Reparse artifact rejected: {path}")
        if path.is_file():
            if not within(path, root):
                raise ValueError(f"Artifact escapes root: {path}")
            files[path.relative_to(root).as_posix()] = sha(path)
    if not files:
        raise ValueError("Artifact bundle is empty")
    return files


def assembly_map(items):
    result = {}
    for item in items:
        name = item["name"]
        if name in result or not HASH.fullmatch(item["sha256"]) or not item.get("mvid") or not item.get("fullName"):
            raise ValueError("Ambiguous or incomplete actual loaded-assembly evidence")
        result[name] = item
    if not REQUIRED <= result.keys():
        raise ValueError(f"Missing mandatory child bindings: {REQUIRED - result.keys()}")
    return result


def child_snapshot(row, attempt):
    children, finals = list(attempt.glob("child-*.json")), list(attempt.glob("loaded-final-*.json"))
    if len(children) != 1 or len(finals) != 1:
        raise ValueError("Exactly one setup and one post-workload child snapshot are required")
    initial, final = read(children[0]), read(finals[0])
    lifetime = row["Case"].get("Lifetime") or "Default"
    if (final.get("schemaVersion") != 1 or final.get("phase") != "process-exit-after-workload" or
        final["pid"] != initial["pid"] or final["scenario"] != row["Case"]["Type"] or
        final["lifetime"] != lifetime or final["initialEvidenceSha256"] != sha(children[0])):
        raise ValueError("Post-workload evidence is absent, failed or not bound to this case/child")
    identity = initial.get("processIdentity")
    if (not isinstance(identity, dict) or identity.get("pid") != initial["pid"] or
        identity.get("platform") not in {"windows", "linux"} or not identity.get("startToken") or
        final.get("processIdentity") != identity):
        raise ValueError("Post-workload evidence has missing or changed process identity")
    try:
        validate_process_identity(identity)
    except RuntimeError as error:
        raise ValueError("Post-workload evidence has unidentifiable process identity") from error
    loaded = assembly_map(final["assemblies"])
    initial_map = assembly_map(initial["assemblies"])
    if any(name not in loaded or loaded[name] != item for name, item in initial_map.items()):
        raise ValueError("A loaded binding changed after setup")
    if not COMPETITORS[row["Library"]] <= loaded.keys():
        raise ValueError("Missing competitor binding for the selected case")
    runtime = final["runtimeEvidence"]
    if runtime["framework"] != final["runtime"] or runtime["controls"].keys() != CONTROLS:
        raise ValueError("Runtime/configuration evidence is incomplete")
    return final, finals[0], loaded


def implementation_identity(project):
    names = ("ArtifactGate.cs", "ArtifactEvidence.cs", "Program.cs", "ChildEvidence.cs")
    return digest({name: sha(project / name) for name in names})


def semantic_identity(final, session, project, runtime, bundle_files):
    config = runtime["runtimeConfig"]
    deps = runtime["deps"]
    for item in (config, deps, *runtime["appDeps"], *runtime["nativeRuntime"]):
        if sha(item["path"]) != item["sha256"]:
            raise ValueError("Measured runtime/dependency configuration changed")
    if not re.fullmatch(r"\d+\.\d+\.\d+", runtime["frameworkVersion"]):
        raise ValueError("Unsupported exact runtime version; no fallback allowed")
    if Path(runtime["hostPath"]).name.lower() not in {"dotnet", "dotnet.exe"} or sha(runtime["hostPath"]) != runtime["hostSha256"]:
        raise ValueError("The measured dotnet executable is unavailable or changed")
    return {
        "session": session["sessionId"], "revision": session["revision"], "sourceDigest": session["sourceDigest"],
        "sdk": session["sdk"], "protocol": PROTOCOL, "gateImplementationSha256": implementation_identity(project),
        "gateDriverSha256": sha(Path(__file__)),
        "bundleFiles": dict(bundle_files),
        "assemblies": sorted([item["name"], item["fullName"], item["mvid"], item["sha256"]]
                             for item in final["assemblies"]),
        "runtime": {key: value for key, value in runtime.items() if key not in ("runtimeConfig", "deps", "appDeps")},
        "runtimeConfig": read(config["path"]), "deps": read(deps["path"]),
        "appDeps": sorted(item["sha256"] for item in runtime["appDeps"]),
    }


def mapped(path, build_root, bundle, framework_root):
    path = Path(path).resolve()
    if within(path, build_root):
        return str(bundle / path.relative_to(build_root))
    if within(path, framework_root):
        return str(path)
    raise ValueError(f"A loaded dependency escapes the BDN output/original framework: {path}")


def prepare_bundle(row, attempt, final, project):
    data = read(attempt / "build-artifacts.json")
    reports = data.get("reports", [])
    if (data.get("schemaVersion") != 1 or len(reports) != 1 or reports[0].get("buildSuccess") is not True or
        reports[0]["type"] != row["Case"]["Type"] or reports[0]["method"] != row["Case"]["Method"]):
        raise ValueError("Missing/substituted structured BDN build identity")
    paths = reports[0]["paths"]
    build_root = Path(paths["buildArtifactsDirectoryPath"]).resolve()
    binaries = Path(paths["binariesDirectoryPath"]).resolve()
    if not within(build_root, project / "bin") or not within(binaries, build_root):
        raise ValueError("BDN build path is outside the owned benchmark output")
    for name in ("projectFilePath", "programCodePath", "executablePath"):
        if not within(paths[name], build_root) or not Path(paths[name]).is_file():
            raise ValueError("Generated project/source/host was not retained")
    if within(attempt, project.parent.parent):
        raise ValueError("Store artifact-gate measurements outside the source worktree")
    bundle = attempt / "bundle"
    if bundle.exists():
        raise ValueError("Existing artifact bundle must be checked through its binding; never overwrite")
    source_files = tree_hashes(build_root)
    shutil.copytree(filesystem_path(build_root), filesystem_path(bundle))
    if tree_hashes(build_root) != source_files or tree_hashes(bundle) != source_files:
        raise ValueError("Generated artifacts changed while archiving")
    runtime = json.loads(json.dumps(final["runtimeEvidence"]))
    framework = Path(runtime["frameworkDirectory"]).resolve()
    expected = []
    for original in final["assemblies"]:
        item = dict(original, path=mapped(original["path"], build_root, bundle, framework))
        if sha(item["path"]) != item["sha256"]:
            raise ValueError("Saved DLL differs from actual child's evidence")
        expected.append(item)
    for name in ("runtimeConfig", "deps"):
        runtime[name]["path"] = mapped(runtime[name]["path"], build_root, bundle, framework)
        if not within(runtime[name]["path"], bundle):
            raise ValueError("Child runtimeconfig/deps must be inside the saved bundle")
        if sha(runtime[name]["path"]) != runtime[name]["sha256"]:
            raise ValueError("Saved runtime configuration differs from the measured child's evidence")
    for item in runtime["appDeps"]:
        item["path"] = mapped(item["path"], build_root, bundle, framework)
        if sha(item["path"]) != item["sha256"]:
            raise ValueError("Saved dependency context differs from measured child")
    return bundle, source_files, expected, runtime, build_root


def verify_runtime(actual, expected):
    ordinary = ("framework", "frameworkVersion", "architecture", "osArchitecture", "frameworkDirectory",
                "hostPath", "hostSha256", "serverGC", "latencyMode", "controls")
    if any(actual.get(key) != expected[key] for key in ordinary):
        raise ValueError("Gate runtime/architecture/configuration differs from measured child")
    for key in ("runtimeConfig", "deps"):
        if actual[key]["sha256"] != expected[key]["sha256"] or not same_path(actual[key]["path"], expected[key]["path"]):
            raise ValueError("Gate used a different runtimeconfig/deps file")
    for key in ("appDeps", "nativeRuntime"):
        if {(os.path.normcase(str(Path(item["path"]).resolve())), item["sha256"]) for item in actual[key]} != {
            (os.path.normcase(str(Path(item["path"]).resolve())), item["sha256"]) for item in expected[key]}:
            raise ValueError("Gate used a different actual dependency context/native runtime")


def verify_gate(request_path, proof_path):
    request, proof = read(request_path), read(proof_path)
    for key in ("protocol", "session", "case", "childPid", "childProofSha256",
                "gateImplementationSha256", "gateDriverSha256", "bundleFiles"):
        if proof.get(key) != request[key]:
            raise ValueError(f"Gate proof identity differs: {key}")
    if (proof.get("schemaVersion") != 1 or proof.get("benchmarkGateCases") != 394 or
        proof.get("bundleBeforeAndAfterVerified") is not True or proof.get("requestHash") != sha(request_path)):
        raise ValueError("Gate proof is incomplete")
    outcomes = read(proof_path.parent / "correctness.json")
    if (len(outcomes) != 394 or any(item.get("status") != "Passed" for item in outcomes) or
        proof["correctnessSha256"] != sha(proof_path.parent / "correctness.json")):
        raise ValueError("394-case benchmark gate evidence changed/failed")
    if tree_hashes(request["bundleRoot"]) != request["bundleFiles"]:
        raise ValueError("Saved gate bundle is missing or substituted")
    actual = assembly_map(proof["assemblies"])
    for expected in request["expectedAssemblies"]:
        if expected["name"] not in actual or actual[expected["name"]] != expected:
            raise ValueError("Actual gate loaded a different consumer/product/dependency binding")
    for item in actual.values():
        if sha(item["path"]) != item["sha256"]:
            raise ValueError("Actual gate loaded binding changed after the gate")
    verify_runtime(proof["runtimeEvidence"], request["expectedRuntime"])
    runtime = request["expectedRuntime"]
    for item in (runtime["runtimeConfig"], runtime["deps"], *runtime["appDeps"], *runtime["nativeRuntime"]):
        if sha(item["path"]) != item["sha256"]:
            raise ValueError("Measured dependency context/native runtime changed after the gate")
    if sha(runtime["hostPath"]) != runtime["hostSha256"]:
        raise ValueError("Measured executable changed after the gate")
    return proof


def ensure_artifact_gate(row, attempt, session, project, run_logged, clean_env):
    final, final_path, loaded = child_snapshot(row, attempt)
    bundle, files, expected, runtime, build_root = prepare_bundle(row, attempt, final, project)
    semantic = semantic_identity(final, session, project, runtime, files)
    key = digest(semantic)
    cache_root = attempt.parents[4] / "artifact-gate-cache"
    cache = cache_root / (key + ".json")
    consumer = next(item["path"] for item in expected if item["name"] == "Zendiator.UseCaseBenchmarks")
    request = {"protocol": PROTOCOL, "session": session["sessionId"], "case": canonical(row["Case"]),
               "childProofSha256": sha(final_path), "childPid": final["pid"], "bundleRoot": str(bundle),
               "output": str(attempt / "artifact-gate"), "consumerPath": consumer,
               "runtimeConfigPath": runtime["runtimeConfig"]["path"], "depsPath": runtime["deps"]["path"],
               "expectedRuntime": runtime, "expectedAssemblies": expected, "bundleFiles": files,
               "originalBuildRoot": str(build_root), "buildEvidenceSha256": sha(attempt / "build-artifacts.json"),
               "gateImplementationSha256": semantic["gateImplementationSha256"],
               "gateDriverSha256": semantic["gateDriverSha256"]}
    request_path = attempt / "artifact-gate-request.json"
    write(request_path, request)
    reused = cache.exists()
    if reused:
        saved = read(cache)
        if saved.get("semantic") != semantic or saved.get("key") != key:
            raise ValueError("Artifact gate cache identity was substituted")
        proof_path = Path(saved["proofPath"])
        saved_request = Path(saved["requestPath"])
        if sha(proof_path) != saved["proofSha256"] or sha(saved_request) != saved["requestSha256"]:
            raise ValueError("Cached gate proof is missing or changed")
        if read(saved_request).get("bundleFiles") != files:
            raise ValueError("Cached gate proof covers a different full saved bundle")
        verify_gate(saved_request, proof_path)
    else:
        env = clean_env()
        for name, value in runtime["controls"].items():
            if value is None:
                env.pop(name, None)
            else:
                env[name] = value
        env["COLD_CAPTURE_CHILD"] = "0"
        env["COLD_EVIDENCE_DIR"] = request["output"]
        env.pop("COLD_SKIP_GATE", None)
        args = [runtime["hostPath"], "exec", "--runtimeconfig", request["runtimeConfigPath"],
                "--depsfile", request["depsPath"], "--fx-version", runtime["frameworkVersion"],
                "--roll-forward", "Disable", consumer, "--artifact-gate", str(request_path)]
        # Explicitly run the saved consumer entry; the generated BDN StartupObject is not a gate entry.
        run_logged(args, attempt / "artifact-gate.log", env, cwd=bundle)
        proof_path = attempt / "artifact-gate/artifact-proof.json"
        verify_gate(request_path, proof_path)
        saved_request = request_path
        write(cache, {"key": key, "semantic": semantic, "proofPath": str(proof_path),
                      "proofSha256": sha(proof_path), "requestPath": str(request_path),
                      "requestSha256": sha(request_path)})
    if (reused and tree_hashes(bundle) != files) or sha(final_path) != request["childProofSha256"]:
        raise ValueError("Current case's saved bundle or child evidence changed during gate/cache validation")
    binding = {"schemaVersion": 1, "session": session["sessionId"],
          "case": canonical(row["Case"]), "childPid": final["pid"], "childProofSha256": sha(final_path),
          "key": key, "semantic": semantic, "requestPath": str(request_path), "requestSha256": sha(request_path),
          "gateRequestPath": str(saved_request), "gateRequestSha256": sha(saved_request),
          "proofPath": str(proof_path), "proofSha256": sha(proof_path), "proofReused": reused,
          "bundleBeforeAndAfterVerified": True}
    binding_path = attempt / "artifact-binding.json"
    write(binding_path, binding)
    return binding_result(binding_path, binding)


def check_artifact_binding(row, attempt, session, project):
    path = attempt / "artifact-binding.json"
    binding = read(path)
    final, final_path, _ = child_snapshot(row, attempt)
    request_path = Path(binding["requestPath"])
    request = read(request_path)
    semantic = semantic_identity(final, session, project, request["expectedRuntime"], request["bundleFiles"])
    if (binding.get("schemaVersion") != 1 or binding.get("session") != session["sessionId"] or
        binding.get("case") != canonical(row["Case"]) or binding.get("childPid") != final["pid"] or
        binding.get("childProofSha256") != sha(final_path) or binding.get("semantic") != semantic or
        binding.get("key") != digest(semantic) or binding.get("bundleBeforeAndAfterVerified") is not True):
        raise ValueError("Artifact binding is not tied to this case/child/session/implementation")
    request_path, gate_request = Path(binding["requestPath"]), Path(binding["gateRequestPath"])
    proof_path = Path(binding["proofPath"])
    for file, key in ((request_path, "requestSha256"), (gate_request, "gateRequestSha256"), (proof_path, "proofSha256")):
        if sha(file) != binding[key]:
            raise ValueError("Artifact binding refers to missing/substituted proof")
    request, proven_request = read(request_path), read(gate_request)
    if (not same_path(request_path, attempt / "artifact-gate-request.json") or
        request["session"] != session["sessionId"] or request["case"] != canonical(row["Case"]) or
        request["childPid"] != final["pid"] or request["childProofSha256"] != sha(final_path) or
        request["buildEvidenceSha256"] != sha(attempt / "build-artifacts.json") or
        not same_path(request["bundleRoot"], attempt / "bundle") or
        (not same_path(request_path, gate_request) and tree_hashes(request["bundleRoot"]) != request["bundleFiles"])):
        raise ValueError("Saved current-case request/bundle identity changed")
    runtime = request["expectedRuntime"]
    original_runtime = final["runtimeEvidence"]
    for name in ("framework", "frameworkVersion", "architecture", "osArchitecture", "frameworkDirectory", "hostPath",
                 "hostSha256", "serverGC", "latencyMode", "controls", "nativeRuntime"):
        if runtime[name] != original_runtime[name]:
            raise ValueError("Saved gate request changed measured runtime/architecture/configuration")
    for name in ("runtimeConfig", "deps"):
        if (runtime[name]["sha256"] != original_runtime[name]["sha256"] or
            not within(runtime[name]["path"], request["bundleRoot"])):
            raise ValueError("Saved runtimeconfig/deps does not represent actual child")
    if sorted(item["sha256"] for item in runtime["appDeps"]) != sorted(item["sha256"] for item in original_runtime["appDeps"]):
        raise ValueError("Saved dependency context changed")
    for original in final["assemblies"]:
        expected = next(item for item in request["expectedAssemblies"] if item["name"] == original["name"])
        target = mapped(original["path"], Path(request["originalBuildRoot"]),
                        Path(request["bundleRoot"]), Path(runtime["frameworkDirectory"]))
        if not same_path(expected["path"], target) or sha(target) != expected["sha256"]:
            raise ValueError("Current-case saved assembly path/content was substituted")
    consumer = next(item["path"] for item in request["expectedAssemblies"] if item["name"] == "Zendiator.UseCaseBenchmarks")
    if (not same_path(request["consumerPath"], consumer) or not within(consumer, request["bundleRoot"]) or
        not same_path(request["runtimeConfigPath"], runtime["runtimeConfig"]["path"]) or
        not same_path(request["depsPath"], runtime["deps"]["path"])):
        raise ValueError("Consumer entry or runtime configuration path changed")
    identity = lambda items: sorted((item["name"], item["fullName"], item["mvid"], item["sha256"]) for item in items)
    if (identity(request["expectedAssemblies"]) != identity(final["assemblies"]) or
        identity(request["expectedAssemblies"]) != identity(proven_request["expectedAssemblies"]) or
        proven_request.get("bundleFiles") != request["bundleFiles"] or
        proven_request["session"] != session["sessionId"] or
        proven_request["gateImplementationSha256"] != semantic["gateImplementationSha256"] or
        proven_request["gateDriverSha256"] != semantic["gateDriverSha256"]):
        raise ValueError("Cached proof covers a different semantic artifact/gate implementation")
    verify_gate(gate_request, proof_path)
    return binding_result(path, binding)


def binding_result(path, binding):
    return {"bindingSha256": sha(path), "childFinalSha256": binding["childProofSha256"], "key": binding["key"],
            "proofSha256": binding["proofSha256"], "proofReused": binding["proofReused"],
            "scope": "394 benchmark gate; notification-contract/full-suite evidence remains separate"}
