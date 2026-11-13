"""Alternate immutable revisions using each worktree's verified single-case worker."""

import argparse
import math
import os
import platform
import re
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

from artifact_gate import COMPETITORS, digest, filesystem_path, glob_paths, read, resolve_path, sha, write
from interleaved_plan import identity, make_ab_plan, normalize_case
from run_interleaved import PROFILE, REPO, check_owned_markers, clean_env, command, run_logged


PROTOCOL = "zendiator-revision-ab-v1"
SIDES = ("baseline", "candidate")
RUNTIME_FIELDS = ("framework", "frameworkVersion", "architecture", "osArchitecture", "frameworkDirectory",
                  "hostPath", "hostSha256", "serverGC", "latencyMode", "controls", "nativeRuntime")


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def tree_entries(tree):
    entries = {}
    for entry in tree.split("\0"):
        if entry:
            metadata, name = entry.split("\t", 1)
            if name in entries:
                raise ValueError("Duplicate tracked Git tree entry")
            entries[name] = metadata
    return entries


def inspect_source(root, revision):
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("A/B revisions require full lowercase commit SHAs")
    if resolve_path(command(["git", "rev-parse", "--show-toplevel"], cwd=root)) != root:
        raise ValueError(f"Not an exact worktree root: {root}")
    if command(["git", "rev-parse", "HEAD"], cwd=root) != revision:
        raise ValueError(f"Worktree revision changed: {root}")
    if command(["git", "status", "--porcelain=v1", "-uall"], cwd=root):
        raise ValueError(f"A/B source worktree must be clean: {root}")
    entries = tree_entries(command(["git", "ls-tree", "-r", "--full-tree", "-z", "HEAD"], cwd=root))
    controls = {name: blob for name, blob in entries.items() if name.startswith("benchmarks/") or
                name in ("global.json", "NuGet.Config", "nuget.config") or name.endswith((".props", ".targets", ".csproj"))}
    source = {name: sha(root / name) for name in entries if name.startswith(("src/", "benchmarks/")) or name in controls}
    if not source or not controls:
        raise ValueError("A/B worktree has no tracked source or benchmark controls")
    return {"revision": revision, "sourceDigest": digest(source), "controls": controls}


def check_sources(roots, revisions, expected=None):
    current = {side: inspect_source(roots[side], revisions[side]) for side in SIDES}
    if current["baseline"]["controls"] != current["candidate"]["controls"]:
        raise ValueError("Benchmark sources, infrastructure, project inputs or package locks differ between revisions")
    if expected is not None and current != expected:
        raise ValueError("A/B immutable source identity changed")
    return current


def finite_number(value, name):
    if type(value) not in (int, float) or not math.isfinite(value):
        raise ValueError(f"Missing or nonfinite {name}")
    return value


def statistics(result):
    stats = result.get("Statistics") or {}
    interval = stats.get("ConfidenceInterval") or {}
    mean = finite_number(stats.get("Mean"), "mean")
    lower = finite_number(interval.get("Lower"), "confidence lower bound")
    upper = finite_number(interval.get("Upper"), "confidence upper bound")
    margin = finite_number(interval.get("Margin"), "confidence margin")
    allocated = finite_number((result.get("Memory") or {}).get("BytesAllocatedPerOperation"), "allocated bytes")
    level = interval.get("Level")
    if ((type(level) not in (int, float, str)) or isinstance(level, str) and not level or
            type(level) in (int, float) and not math.isfinite(level)):
        raise ValueError("Missing or nonfinite confidence level")
    count = stats.get("N")
    if type(count) is not int or count < 2 or mean <= 0 or allocated < 0 or margin < 0 or not lower <= mean <= upper:
        raise ValueError("Invalid BDN statistics")
    return {"MeanNs": mean, "N": count, "ConfidenceLevel": level, "ConfidenceLowerNs": lower,
            "ConfidenceUpperNs": upper, "ConfidenceMarginNs": margin, "AllocatedBytes": allocated}


def compare(baseline, candidate):
    if baseline["ConfidenceLevel"] != candidate["ConfidenceLevel"]:
        raise ValueError("A/B confidence levels differ")
    classification = ("improvement" if candidate["ConfidenceUpperNs"] < baseline["ConfidenceLowerNs"] else
                      "regression" if candidate["ConfidenceLowerNs"] > baseline["ConfidenceUpperNs"] else
                      "overlap")
    return {"classification": classification, "meanRatioCandidateOverBaseline": candidate["MeanNs"] / baseline["MeanNs"],
            "allocatedBytesDelta": candidate["AllocatedBytes"] - baseline["AllocatedBytes"]}


def completed_case(output, row, worker_session=None):
    directory = output / "runs/custom/cases" / f'{row["CaseIndex"]:04d}'
    marker = directory / "complete.json"
    record = read(marker)
    if identity(record["case"]) != identity(row["Case"]):
        raise ValueError("A/B worker completed a different case")
    attempt = directory / record["attempt"]
    reports = list(glob_paths(attempt / "results", "*-full.json"))
    if len(reports) != 1 or sha(reports[0]) != record["reportSha256"]:
        raise ValueError("A/B worker report changed")
    binding = attempt / "artifact-binding.json"
    if sha(binding) != record["artifactGate"]["bindingSha256"]:
        raise ValueError("A/B worker artifact binding changed")
    if worker_session is not None and read(binding)["session"] != worker_session:
        raise ValueError("A/B artifact binding belongs to a different worker session")
    evidence = next(glob_paths(attempt, "loaded-final-*.json"))
    if sha(evidence) != record["artifactGate"]["childFinalSha256"]:
        raise ValueError("A/B worker final loaded-assembly evidence changed")
    data = read(reports[0])
    if len(data["Benchmarks"]) != 1:
        raise ValueError("A/B report must contain one measured case")
    return {"completePath": str(marker), "completeSha256": sha(marker), "reportPath": str(reports[0]),
            "reportSha256": sha(reports[0]), "bindingSha256": sha(binding),
            "statistics": statistics(data["Benchmarks"][0]), "child": read(evidence)}


def check_pair(left, right, row):
    a, b = left["child"], right["child"]
    for child in (a, b):
        runtime = child["runtimeEvidence"]
        if (runtime.get("frameworkVersion") != "10.0.14" or runtime.get("architecture") != "X64" or
                runtime.get("serverGC") is not False):
            raise ValueError("A/B requires .NET 10.0.14 x64 with workstation GC")
    if {name: a["runtimeEvidence"][name] for name in RUNTIME_FIELDS} != {
            name: b["runtimeEvidence"][name] for name in RUNTIME_FIELDS}:
        raise ValueError("A/B cases ran under different runtime controls")
    required = COMPETITORS[row["Library"]] | {"Microsoft.Extensions.DependencyInjection",
               "Microsoft.Extensions.DependencyInjection.Abstractions", "System.Private.CoreLib"}
    bindings = [{item["name"]: item["sha256"] for item in child["assemblies"]} for child in (a, b)]
    if any(name not in bindings[0] or name not in bindings[1] or bindings[0][name] != bindings[1][name] for name in required):
        raise ValueError("A/B cases loaded different peer, DI or core runtime assemblies")


def worker(roots, outputs, matrix, sdk, reverse, side, phase, log, index=None):
    script = roots[side] / "benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py"
    args = [sys.executable, str(script), "--group", "custom", "--matrix", str(roots[side] / matrix),
            "--output", str(outputs[side]), "--expect-sdk", sdk, "--phase", phase]
    if reverse:
        args.append("--reverse")
    if phase != "prepare":
        args.append("--resume")
    if index is not None:
        args.extend(["--case-index", str(index)])
    run_logged(args, log, clean_env(), cwd=roots[side])


def verify_trace(output, schedule, records, workers):
    if len(records) > len(schedule):
        raise ValueError("A/B trace contains extra cases")
    end = None
    for row, record in zip(schedule, records):
        if record.get("row") != row or record.get("state") != "complete":
            raise ValueError("A/B trace has an uncertain or reordered operation; do not resume")
        start, stop = record["startMonotonicNs"], record["endMonotonicNs"]
        if type(start) is not int or type(stop) is not int or start >= stop or end is not None and start < end:
            raise ValueError("A/B actual monotonic execution order is invalid")
        if not record.get("startUtc") or not record.get("endUtc"):
            raise ValueError("A/B actual UTC timestamps are missing")
        expected_path = output / row["Side"] / "runs/custom/cases" / f'{row["CaseIndex"]:04d}' / "complete.json"
        if resolve_path(record["completePath"]) != resolve_path(expected_path) or (
                record.get("workerSessionId") != workers[row["Side"]]["sessionId"]):
            raise ValueError("A/B trace changed the side, case path or worker session")
        if sha(record["completePath"]) != record["completeSha256"]:
            raise ValueError("A/B completed-case marker changed after its recorded operation")
        end = stop
    check_owned_markers(output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--baseline-sha", required=True)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--matrix", type=Path, required=True, help="Same relative matrix path in both worktrees")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--expect-sdk", default="10.0.404")
    parser.add_argument("--reverse", action="store_true", help="New independent block in the complete reverse order")
    parser.add_argument("--resume", action="store_true")
    args = parser.parse_args()
    if os.name != "nt":
        raise RuntimeError("A/B workers currently require Windows nested Job process containment")
    roots = {side: resolve_path(getattr(args, side)) for side in SIDES}
    revisions = {side: getattr(args, side + "_sha") for side in SIDES}
    if roots["baseline"] == roots["candidate"] or any(roots[a] in roots[b].parents for a, b in (SIDES, SIDES[::-1])):
        raise ValueError("A/B requires separate, non-nested worktrees")
    matrix = args.matrix
    if matrix.is_absolute() or ".." in matrix.parts or matrix.parts[:2] != ("benchmarks", "performance-plan"):
        raise ValueError("A/B matrix must be a relative benchmarks/performance-plan path")
    sources = check_sources(roots, revisions)
    controller = inspect_source(REPO, command(["git", "rev-parse", "HEAD"]))
    if controller["controls"] != sources["baseline"]["controls"]:
        raise ValueError("Controller and worker benchmark implementations differ")
    cases = [normalize_case(case) for case in read(roots["baseline"] / matrix)]
    schedule = make_ab_plan(cases, args.reverse)
    output = resolve_path(args.output)
    if any(output == root or root in output.parents for root in (*roots.values(), REPO)):
        raise ValueError("A/B output must be outside all source worktrees")
    outputs = {side: output / side for side in SIDES}
    current = {"protocol": PROTOCOL, "roots": {side: str(root) for side, root in roots.items()}, "sources": sources,
               "matrix": matrix.as_posix(), "matrixSha256": sha(roots["baseline"] / matrix), "sdk": args.expect_sdk,
               "profile": PROFILE, "reverse": args.reverse, "planSha256": digest(schedule),
               "pythonSha256": sha(sys.executable), "host": dict(platform.uname()._asdict()), "processorCount": os.cpu_count()}
    session_path = output / "session.json"
    if args.resume:
        session = read(session_path)
        if any(session.get(key) != value for key, value in current.items()) or not session.get("sessionId"):
            raise ValueError("A/B session inputs changed; do not resume")
        if read(output / "execution-plan.json") != schedule:
            raise ValueError("A/B plan changed; do not resume")
        check_owned_markers(output)
    else:
        if filesystem_path(output).exists():
            raise ValueError("A/B output already exists; use a new directory")
        filesystem_path(output).mkdir(parents=True)
        session = dict(current, sessionId=uuid.uuid4().hex, createdUtc=utc_now())
        write(session_path, session)
        write(output / "execution-plan.json", schedule)
        for side in SIDES:
            worker(roots, outputs, matrix, args.expect_sdk, args.reverse, side, "prepare", output / f"prepare-{side}.log")
        session["workers"] = {side: read(outputs[side] / "session.json") for side in SIDES}
        if any(session["workers"][side]["runtime"] != ".NET 10.0.14" for side in SIDES) or (
                session["workers"]["baseline"]["diSha256"] != session["workers"]["candidate"]["diSha256"]):
            raise ValueError("Prepared A/B runtime or DI conditions differ")
        write(session_path, session)
    if "workers" not in session:
        raise ValueError("A/B preparation did not complete; retain evidence and start a new output")
    if any(read(outputs[side] / "session.json") != session["workers"][side] for side in SIDES):
        raise ValueError("A/B worker session identity changed")
    records = [read(path) for path in sorted(glob_paths(output / "steps", "*.json"))]
    verify_trace(output, schedule, records, session["workers"])
    for row in schedule[len(records):]:
        check_sources(roots, revisions, sources)
        side = row["Side"]
        path = output / "steps" / f'{row["Ordinal"]:04d}.json'
        existing = outputs[side] / "runs/custom/cases" / f'{row["CaseIndex"]:04d}' / "complete.json"
        if filesystem_path(existing).exists():
            raise ValueError("An unrecorded worker result exists; do not substitute it into the A/B trace")
        if records and time.monotonic_ns() < records[-1]["endMonotonicNs"]:
            raise ValueError("A/B monotonic clock restarted; begin a new block")
        record = {"row": row, "workerSessionId": session["workers"][side]["sessionId"], "state": "running",
                  "startUtc": utc_now(), "startMonotonicNs": time.monotonic_ns()}
        write(path, record)
        worker(roots, outputs, matrix, args.expect_sdk, args.reverse, side, "case",
               output / "logs" / f'{row["Ordinal"]:04d}.log', row["CaseIndex"])
        evidence = completed_case(outputs[side], row, session["workers"][side]["sessionId"])
        record.update(state="complete", endUtc=utc_now(), endMonotonicNs=time.monotonic_ns(),
                      completePath=evidence["completePath"], completeSha256=evidence["completeSha256"],
                      reportSha256=evidence["reportSha256"], bindingSha256=evidence["bindingSha256"])
        write(path, record)
        records.append(record)
        print(f'{row["Ordinal"]}/{len(schedule)}: {side} {row["Case"]["Type"]}.{row["Case"]["Method"]}', flush=True)
    verify_trace(output, schedule, records, session["workers"])
    for side in SIDES:
        worker(roots, outputs, matrix, args.expect_sdk, args.reverse, side, "finish", output / f"finish-{side}.log")
    check_sources(roots, revisions, sources)
    pairs = {}
    for row in schedule:
        pairs.setdefault(identity(row["Case"]), {})[row["Side"]] = row
    results = []
    for pair in pairs.values():
        evidence = {side: completed_case(outputs[side], pair[side], session["workers"][side]["sessionId"]) for side in SIDES}
        row = pair["baseline"]
        check_pair(evidence["baseline"], evidence["candidate"], row)
        values = {side: evidence[side]["statistics"] for side in SIDES}
        results.append({"case": row["Case"], "library": row["Library"], "comparisonKey": row["ComparisonKey"],
                        **values, **compare(values["baseline"], values["candidate"]),
                        "reports": {side: {name: evidence[side][name] for name in ("reportPath", "reportSha256", "bindingSha256")} for side in SIDES}})
    write(output / "comparison.json", {"protocol": PROTOCOL, "sessionId": session["sessionId"], "reverse": args.reverse,
          "planSha256": digest(schedule), "results": results, "adoptionDecision": "not-automated"})
    write(output / "outcome.json", {"cases": len(schedule), "failures": 0, "validationErrors": 0,
          "comparisonSha256": sha(output / "comparison.json"), "finishedUtc": utc_now()})
    print(f"Verified A/B block: {output}")


if __name__ == "__main__":
    main()
