"""Run one BDN case per process in a verified cross-library order.

This is separate from Run-Comparison.ps1: its historical type-block results are
never reused or merged into an interleaved run.
"""

import argparse
import hashlib
import json
import os
import re
import shutil
import signal
import subprocess
import uuid
from pathlib import Path

from interleaved_plan import identity, make_plan, normalize_case, verify_log
from owned_process import OwnedCommand, process_alive, process_identity
from artifact_gate import PROTOCOL as ARTIFACT_GATE_PROTOCOL, ensure_artifact_gate, check_artifact_binding


PROJECT = Path(__file__).resolve().parent
REPO = PROJECT.parent.parent
GROUPS = ("send", "features", "streams", "relay")
PROFILE = "Release; affinity 1; 20 warmup; 12 measurement; 500 ms requested iteration; 1 launch"


def command(args, cwd=REPO):
    return subprocess.check_output(args, cwd=cwd, text=True).strip()


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temp.replace(path)


def source_digest():
    files = command(["git", "ls-files", "--", "src", "benchmarks"]).splitlines()
    digest = hashlib.sha256()
    for name in sorted(files):
        path = REPO / name
        digest.update(name.encode("utf-8"))
        digest.update(bytes.fromhex(sha(path)))
    return digest.hexdigest().upper()


def check_project_discovery():
    expected = (PROJECT / "Zendiator.UseCaseBenchmarks.csproj").resolve()
    matches = sorted(path.resolve() for path in REPO.rglob(expected.name))
    if matches != [expected]:
        found = ", ".join(str(path) for path in matches) or "none"
        raise ValueError("BDN requires exactly one benchmark project below this worktree; "
                         f"found: {found}. Use a clean sibling worktree and a new output root.")


def check_binary_identity(session):
    paths = (("runtimeSha256", REPO / "src/Zendiator/bin/Release/net10.0/Zendiator.dll"),
             ("benchmarkSha256", PROJECT / "bin/Release/net10.0/Zendiator.UseCaseBenchmarks.dll"),
             ("generatorSha256", REPO / "src/Zendiator.SourceGenerator/bin/Release/netstandard2.0/Zendiator.SourceGenerator.dll"))
    if any(sha(path) != session[key] for key, path in paths):
        raise ValueError("Built DLL identity changed")


def run_logged(args, path, env, cwd=PROJECT):
    path.parent.mkdir(parents=True, exist_ok=True)
    marker = path.with_name(path.name + ".process.json")
    record = {"schemaVersion": 2, "pid": None, "command": args,
              "state": "preparing", "cleanupVerified": False}
    # Persist uncertainty before launching: a crash without ChildEvidence must block resume.
    write_json(marker, record)
    with path.open("w", encoding="utf-8") as log:
        owned = OwnedCommand(args, cwd, env, log)
        old_term = signal.getsignal(signal.SIGTERM)

        def interrupted(*_):
            raise KeyboardInterrupt

        signal.signal(signal.SIGTERM, interrupted)
        try:
            owned.start()
            record.update(pid=owned.process.pid, processIdentity=process_identity(owned.process.pid),
                          containment=owned.kind, state="running")
            write_json(marker, record)
            owned.release()
            code = owned.process.wait()
            if not owned.wait_empty(2):
                raise RuntimeError("Command exited with live owned descendants")
            if code:
                raise RuntimeError(f"Command failed ({code}); retained log: {path}")
            record.update(state="exited", exitCode=code, cleanupVerified=True)
            write_json(marker, record)
        except BaseException as error:
            # A second interrupt must not skip cleanup or produce a verified marker.
            old_int = signal.signal(signal.SIGINT, signal.SIG_IGN)
            signal.signal(signal.SIGTERM, signal.SIG_IGN)
            try:
                try:
                    verified = owned.stop_and_wait()
                except Exception as cleanup_error:
                    verified = False
                    record["cleanupError"] = str(cleanup_error)
                record.update(state="interrupted" if isinstance(error, KeyboardInterrupt) else "failed",
                              exitCode=owned.process.returncode if owned.process else None,
                              cleanupVerified=verified)
                write_json(marker, record)
            finally:
                signal.signal(signal.SIGINT, old_int)
            raise
        finally:
            try:
                owned.close()
            finally:
                signal.signal(signal.SIGTERM, old_term)



def check_owned_markers(directory):
    for marker in directory.rglob("*.process.json"):
        state = json.loads(marker.read_text(encoding="utf-8"))
        if (state.get("schemaVersion") != 2 or state.get("cleanupVerified") is not True or
            state.get("state") not in ("exited", "interrupted", "failed")):
            raise RuntimeError(f"Owned process tree termination is unverified; do not resume: {marker}")


def check_abandoned(case_dir):
    for attempt in case_dir.glob("attempt-*"):
        if not list(attempt.glob("*.process.json")):
            raise RuntimeError(f"No process containment evidence; do not resume: {attempt}")
        check_owned_markers(attempt)
        for evidence in attempt.glob("child-*.json"):
            child = json.loads(evidence.read_text(encoding="utf-8"))
            identity = child.get("processIdentity")
            if not isinstance(identity, dict) or identity.get("pid") != child["pid"]:
                raise RuntimeError(f"Missing or inconsistent child process identity; do not resume: {evidence}")
            if process_alive(identity):
                raise RuntimeError(f"Prior BDN child {child['pid']} is still running: {evidence}")


def check_resume_ownership(output):
    # Include restore/build/gate and completed cases, not just an unfinished BDN case.
    for name in ("restore.log.process.json", "build.log.process.json"):
        if not (output / name).exists():
            raise RuntimeError(f"Missing build containment evidence; do not resume: {output / name}")
    check_owned_markers(output)
    for cases in output.glob("runs/*/cases"):
        for case_dir in cases.iterdir():
            if case_dir.is_dir():
                check_abandoned(case_dir)


def clean_env():
    env = os.environ.copy()
    for name in ("COLD_RUN", "COLD_MATRIX_FILE", "COLD_CASE", "COLD_FORMAL", "COLD_PINNED",
                 "COLD_EXPECT_CHILD_Z_SHA", "COLD_SKIP_GATE", "COLD_GATE_PROOF", "COLD_SINGLE_CASE",
                 "COLD_SCOPED_PAIR", "COLD_COMPETITORS", "COLD_ONLY"):
        env.pop(name, None)
    return env


def build_env():
    env = clean_env()
    env.update(MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0", UseSharedCompilation="false")
    return env


def gate_valid(path):
    if not path.exists():
        return False
    records = json.loads(path.read_text(encoding="utf-8"))
    return len(records) == 394 and all(row.get("status") == "Passed" for row in records)


def check_case(row, attempt, session, require_artifact_gate=True):
    outcome = json.loads((attempt / "outcome.json").read_text(encoding="utf-8"))
    if any(outcome.get(key) != value for key, value in (("cases", 1), ("failures", 0), ("validationErrors", 0))):
        raise ValueError(f"Incomplete BDN result: {attempt}")
    verify_log([row], attempt / "run.log")
    if row["Case"]["Method"] == "FirstSend":
        check_firstsend(attempt / "run.log", [row["Case"]])
    children = list(attempt.glob("child-*.json"))
    reports = list((attempt / "results").glob("*-full.json"))
    if len(children) != 1 or len(reports) != 1:
        raise ValueError(f"Expected one child and one report: {attempt}")
    data = json.loads(reports[0].read_text(encoding="utf-8"))
    if len(data["Benchmarks"]) != 1:
        raise ValueError(f"Expected one benchmark result: {reports[0]}")
    result = data["Benchmarks"][0]
    result_case = {"Type": result["Type"], "Method": result["Method"]}
    for item in re.split(r"[,\&]\s*", result.get("Parameters") or ""):
        if "=" not in item:
            continue
        name, value = item.split("=", 1)
        if name == "Count":
            result_case[name] = int(value)
        elif name == "Asynchronous":
            result_case[name] = value == "True"
        elif name == "Lifetime":
            result_case[name] = value
    if identity(result_case) != identity(row["Case"]):
        raise ValueError(f"BDN JSON differs from selected case: {reports[0]}")
    child = json.loads(children[0].read_text(encoding="utf-8"))
    assemblies = {assembly["name"]: assembly["sha256"] for assembly in child["assemblies"]}
    if len(assemblies) != len(child["assemblies"]) or any(not digest for digest in assemblies.values()):
        raise ValueError(f"Duplicate assembly names or missing child SHA: {children[0]}")
    hashes = [assemblies.get("Zendiator")]
    if len(hashes) != 1 or not hashes[0]:
        raise ValueError(f"Missing child product SHA: {children[0]}")
    manifest = json.loads((attempt / "manifest.json").read_text(encoding="utf-8"))
    if (manifest["runtimeSha256"] != session["runtimeSha256"] or
        manifest["assemblySha256"] != session["benchmarkSha256"] or
        manifest["generatorSha256"] != session["generatorSha256"] or
        manifest["gateProofSha256"] != session["gateSha256"][attempt.parents[2].name] or
        manifest["runtime"] != session["runtime"] or
        manifest["diSha256"] != session["diSha256"] or
        child["runtime"] != session["runtime"]):
        raise ValueError(f"Runtime, benchmark, generator, DI, or gate identity changed: {attempt}")
    record = {"attempt": attempt.name, "case": row["Case"], "childSha256": hashes[0],
              "childAssemblies": assemblies, "reportSha256": sha(reports[0]),
              "childEvidenceSha256": sha(children[0])}
    if require_artifact_gate:
        record["artifactGate"] = check_artifact_binding(row, attempt, session, PROJECT)
    return record


def completed(row, case_dir, session):
    marker = case_dir / "complete.json"
    if not marker.exists():
        return None
    saved = json.loads(marker.read_text(encoding="utf-8"))
    if identity(saved["case"]) != identity(row["Case"]):
        raise ValueError(f"Completed case differs from plan: {case_dir}")
    checked = check_case(row, case_dir / saved["attempt"], session)
    if saved != checked:
        raise ValueError(f"Completed evidence changed: {case_dir}")
    return saved


def check_firstsend(log, expected):
    seen = {}
    label = None
    for line in log.read_text(encoding="utf-8").splitlines():
        if line.startswith("// Benchmark: "):
            match = re.match(r"// Benchmark: (\S+\.FirstSend): .*\[Lifetime=([^]]+)\]$", line)
            label = f"{match.group(1)}|{match.group(2)}" if match else None
            if label:
                if "InvocationCount=16384" not in line or "UnrollFactor=1" not in line:
                    raise ValueError(f"FirstSend job changed: {line}")
                seen[label] = []
        elif label and line.startswith("WorkloadActual"):
            match = re.match(r"WorkloadActual\s+\d+: (\d+) op,", line)
            if match:
                seen[label].append(int(match.group(1)))
    wanted = {f'{case["Type"]}.FirstSend|{case["Lifetime"]}' for case in expected if case["Method"] == "FirstSend"}
    if set(seen) != wanted or any(len(values) != 12 or set(values) != {16384} for values in seen.values()):
        raise ValueError("FirstSend actual-operation evidence is incomplete")
    return {"passed": True, "cases": len(seen), "actualRows": sum(map(len, seen.values())),
            "operationsPerActualRow": 16384}


def finish_group(run, plan, cases, session):
    expected_names = {f"{i:04d}-full.json" for i in range(1, len(plan) + 1)}
    result_dir = run / "results"
    result_dir.mkdir(exist_ok=True)
    if {path.name for path in result_dir.glob("*-full.json")} - expected_names:
        raise ValueError(f"Unexpected aggregate reports in {result_dir}")
    logs = []
    hashes = set()
    assembly_hashes = {}
    artifact_proofs = []
    for ordinal, row in enumerate(plan, 1):
        record = completed(row, run / "cases" / f"{ordinal:04d}", session)
        if record is None:
            raise ValueError(f"Missing completed case {ordinal}")
        attempt = run / "cases" / f"{ordinal:04d}" / record["attempt"]
        report = next((attempt / "results").glob("*-full.json"))
        child = next(attempt.glob("child-*.json"))
        shutil.copyfile(report, result_dir / f"{ordinal:04d}-full.json")
        shutil.copyfile(child, run / f"child-{ordinal:04d}.json")
        logs.append((attempt / "run.log").read_text(encoding="utf-8"))
        hashes.add(record["childSha256"])
        artifact_proofs.append(record["artifactGate"])
        for name, digest in record["childAssemblies"].items():
            if name.startswith("Zendiator.UseCaseBenchmarks-"):
                continue  # BDN generates a distinct executable for each selected case.
            if name in assembly_hashes and assembly_hashes[name] != digest:
                raise ValueError(f"Child assembly {name} changed within group")
            assembly_hashes[name] = digest
    if len(hashes) != 1:
        raise ValueError(f"Child product DLL differs across cases: {hashes}")
    log = run / "run.log"
    log.write_text("\n".join(logs), encoding="utf-8")
    verify_log(plan, log)
    firstsend = check_firstsend(log, cases) if any(case["Method"] == "FirstSend" for case in cases) else None
    write_json(run / "run-info.json", {"group": run.name, "revision": session["revision"],
               "sdk": session["sdk"], "expectedCases": len(plan), "childCount": len(plan),
               "productHashes": sorted(hashes), "sourceDigest": session["sourceDigest"],
               "childAssemblyHashes": assembly_hashes, "runtime": session["runtime"],
               "executionOrder": "cross-library-interleaved-verified", "firstSendActual": firstsend,
               "artifactGateProtocol": session["artifactGateProtocol"], "artifactGateProofs": artifact_proofs,
               "profile": PROFILE})
    write_json(run / "outcome.json", {"cases": len(plan), "failures": 0, "validationErrors": 0})
    return next(iter(hashes))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--group", choices=("all", *GROUPS, "custom"), default="all")
    parser.add_argument("--matrix", type=Path, help="Required for --group custom; enables a focused comparison")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--resume", action="store_true")
    parser.add_argument("--max-cases", type=int, help="Stop after this many completed cases; leave a resumable partial run")
    parser.add_argument("--expect-sdk", default="10.0.401")
    args = parser.parse_args()
    if (args.group == "custom") != (args.matrix is not None):
        parser.error("--matrix must be supplied exactly when --group custom is selected")
    groups = GROUPS if args.group == "all" else (args.group,)
    matrix_paths = {group: (args.matrix.resolve() if group == "custom" else PROJECT / f"{group}.json") for group in groups}
    plans = {}
    for group in groups:
        cases = [normalize_case(case) for case in json.loads(matrix_paths[group].read_text(encoding="utf-8"))]
        plan, keys = make_plan(cases)
        plans[group] = (cases, plan)
        print(f"{group}: {len(plan)} cases, {keys} comparison keys; first: {plan[0]['Library']}")
    if args.dry_run:
        return
    check_project_discovery()
    if args.max_cases is not None and args.max_cases < 1:
        raise ValueError("--max-cases must be positive")
    if command(["git", "status", "--porcelain=v1", "-uall"]):
        raise ValueError("Commit or remove workspace changes before formal measurement")
    sdk = command(["dotnet", "--version"])
    if sdk != args.expect_sdk:
        raise ValueError(f"SDK {sdk} differs from requested {args.expect_sdk}")
    output = args.output.resolve()
    if output == REPO or REPO in output.parents:
        raise ValueError("The retained child-artifact protocol requires output outside the source worktree")
    if args.resume:
        if not (output / "session.json").exists():
            raise ValueError("No session to resume")
        check_resume_ownership(output)
    elif output.exists():
        raise ValueError(f"Output already exists: {output}")
    else:
        output.mkdir(parents=True)
    current = {"revision": command(["git", "rev-parse", "HEAD"]), "sdk": sdk,
               "sourceDigest": source_digest(), "groups": list(groups),
               "matrixSha256": {group: sha(matrix_paths[group]) for group in groups},
               "profile": PROFILE, "artifactGateProtocol": ARTIFACT_GATE_PROTOCOL}
    if args.resume:
        session = json.loads((output / "session.json").read_text(encoding="utf-8"))
        if not session.get("sessionId"):
            raise ValueError("No session identity for exact-child artifact proofs; cannot resume")
        if any(session.get(key) != value for key, value in current.items()):
            raise ValueError("Revision, SDK, source, group, or matrix changed; cannot resume")
    else:
        run_logged(["dotnet", "restore", str(PROJECT / "Zendiator.UseCaseBenchmarks.csproj"), "--locked-mode",
                    "--disable-build-servers", "-p:UseSharedCompilation=false", "-nodeReuse:false"], output / "restore.log", build_env())
        run_logged(["dotnet", "build", str(PROJECT / "Zendiator.UseCaseBenchmarks.csproj"), "-c", "Release", "--no-restore",
                    "--disable-build-servers", "-p:UseSharedCompilation=false", "-nodeReuse:false"], output / "build.log", build_env())
        session = dict(current)
        session["sessionId"] = uuid.uuid4().hex
        session["runtimeSha256"] = sha(REPO / "src/Zendiator/bin/Release/net10.0/Zendiator.dll")
        session["benchmarkSha256"] = sha(PROJECT / "bin/Release/net10.0/Zendiator.UseCaseBenchmarks.dll")
        session["generatorSha256"] = sha(REPO / "src/Zendiator.SourceGenerator/bin/Release/netstandard2.0/Zendiator.SourceGenerator.dll")
        write_json(output / "session.json", session)
    check_binary_identity(session)

    completed_count = 0
    product_hash = None
    known_assemblies = {}
    for group in groups:
        cases, plan = plans[group]
        run = output / "runs" / group
        run.mkdir(parents=True, exist_ok=True)
        plan_file = run / "execution-plan.json"
        if plan_file.exists():
            if json.loads(plan_file.read_text(encoding="utf-8")) != plan:
                raise ValueError(f"Plan changed: {run}")
        else:
            write_json(plan_file, plan)
        proof = run / "correctness.json"
        if not gate_valid(proof):
            if proof.exists():
                raise ValueError(f"Existing gate evidence is invalid: {proof}")
            env = clean_env()
            env.update(COLD_RUN=str(run), COLD_PINNED="1", COLD_EXPECT_CHILD_Z_SHA="SKIP")
            run_logged(["dotnet", str(PROJECT / "bin/Release/net10.0/Zendiator.UseCaseBenchmarks.dll"), "--validate-only"], run / "gate.log", env)
            if not gate_valid(proof):
                raise ValueError(f"Correctness gate failed: {run}")
        gate_manifest = json.loads((run / "manifest.json").read_text(encoding="utf-8"))
        if (gate_manifest["runtimeSha256"] != session["runtimeSha256"] or
            gate_manifest["assemblySha256"] != session["benchmarkSha256"] or
            gate_manifest["generatorSha256"] != session["generatorSha256"]):
            raise ValueError(f"Gate DLL identity differs from built DLL: {run}")
        for key, value in (("runtime", gate_manifest["runtime"]), ("diSha256", gate_manifest["diSha256"])):
            if key in session and session[key] != value:
                raise ValueError(f"Gate {key} changed during measurement")
            session[key] = value
        session.setdefault("gateSha256", {})
        proof_sha = sha(proof)
        if group in session["gateSha256"] and session["gateSha256"][group] != proof_sha:
            raise ValueError(f"Gate evidence changed: {proof}")
        session["gateSha256"][group] = proof_sha
        write_json(output / "session.json", session)
        for ordinal, row in enumerate(plan, 1):
            case_dir = run / "cases" / f"{ordinal:04d}"
            if completed(row, case_dir, session):
                completed_count += 1
                continue
            if args.max_cases is not None and completed_count >= args.max_cases:
                print(f"Stopped after {completed_count} completed cases; resume with --resume")
                return
            if (source_digest() != session["sourceDigest"] or
                command(["git", "rev-parse", "HEAD"]) != session["revision"] or
                command(["dotnet", "--version"]) != session["sdk"]):
                raise ValueError("Source or SDK changed during measurement")
            check_binary_identity(session)
            check_abandoned(case_dir)
            case_dir.mkdir(parents=True, exist_ok=True)
            attempt = case_dir / f"attempt-{len(list(case_dir.glob('attempt-*'))) + 1:04d}"
            attempt.mkdir()
            one_case = attempt / "matrix.json"
            write_json(one_case, [row["Case"]])
            env = clean_env()
            env.update(COLD_RUN=str(attempt), COLD_MATRIX_FILE=str(one_case), COLD_PINNED="1",
                       COLD_SKIP_GATE="1", COLD_SINGLE_CASE="1", COLD_GATE_PROOF=str(proof), COLD_EXPECT_CHILD_Z_SHA="SKIP")
            run_logged(["dotnet", str(PROJECT / "bin/Release/net10.0/Zendiator.UseCaseBenchmarks.dll"), "--filter", "*"], attempt / "run.log", env)
            check_case(row, attempt, session, require_artifact_gate=False)
            ensure_artifact_gate(row, attempt, session, PROJECT, run_logged, clean_env)
            write_json(case_dir / "complete.json", check_case(row, attempt, session))
            completed_count += 1
            print(f"{group} {ordinal}/{len(plan)}: {row['Library']} {row['Case']['Type']}.{row['Case']['Method']}", flush=True)
        child_hash = finish_group(run, plan, cases, session)
        if product_hash and child_hash != product_hash:
            raise ValueError("Child product DLL differs across groups")
        product_hash = child_hash
        group_assemblies = json.loads((run / "run-info.json").read_text(encoding="utf-8"))["childAssemblyHashes"]
        for name, digest in group_assemblies.items():
            if name in known_assemblies and known_assemblies[name] != digest:
                raise ValueError(f"Child assembly {name} differs across groups")
            known_assemblies[name] = digest
    if source_digest() != session["sourceDigest"] or command(["git", "rev-parse", "HEAD"]) != session["revision"]:
        raise ValueError("Source changed during measurement")
    analysis = ["python", str(PROJECT / "analyze.py"), str(output)]
    if args.group == "custom":
        analysis.extend(["--matrix", str(matrix_paths["custom"])])
    run_logged(analysis, output / "analyze.log", clean_env())
    if args.group == "all":
        run_logged(["python", str(PROJECT / "make_report.py"), str(output)], output / "report.log", clean_env())
    print(f"Verified interleaved run: {output}")


if __name__ == "__main__":
    main()
