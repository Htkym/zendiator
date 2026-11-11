"""Prepare a pinned source tree with hash-verified historical benchmark inputs.

This restores the original ShortCircuit/HPeer bodies. Their admission-only entry
points are retained as .frozen files and replaced by the existing PR10 runner.
No benchmark or product code is executed by this preparation command.
"""
import argparse
import hashlib
import io
import json
import shutil
import subprocess
import tarfile
from pathlib import Path

HOOK_SHA = "55A23A09D4172EE81E4C16E45875CEA9D51C218E76E78B522706B30EEA253A13"
RUNNER_HASHES = {
    "MeasureRunner.cs": "25E488D9D756CD5D57A5E4D28573D853491A9F1ACEB506C13ABC9C9DDCB52345",
    "MeasureSemantics.Short.cs": "3EC3298C32594F5A9EDB616BDBE505C6CBEF1632C2A108DDC544544BB9BD4CEC",
    "MeasureSemantics.H.cs": "70739E1E35272EA3926C626AFB6B2EDF80027D802D5B9F8B8B5369B6EBA028E1",
}
DIRECTORIES = ("benchmarks/ShortCircuitComparison/", "benchmarks/HPeerComparison/")
FILES = ("global.json", "benchmarks/Directory.Build.props",
         "benchmarks/FoundatioReleaseComparison/FoundatioRuntimeEvidence.cs")


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def contained(root, relative):
    target = (root / relative).resolve()
    if not target.is_relative_to(root.resolve()):
        raise ValueError(f"Path escapes its root: {relative}")
    return target


def prepare(repository, revision, historical, hook, output):
    if output.exists():
        raise ValueError(f"Output already exists: {output}")
    overlay = historical / "frozen-overlay"
    manifest = json.loads((overlay / "OVERLAY-MANIFEST.json").read_text(encoding="utf-8"))
    pinned = {row["path"]: row["sha256"] for row in manifest["files"] if row["source"]}
    wanted = sorted(set(FILES) | {path for path in pinned if path.startswith(DIRECTORIES)})
    inputs = []
    for relative in wanted:
        source = contained(overlay, relative)
        if relative not in pinned or sha(source) != pinned[relative]:
            raise ValueError(f"Historical input hash mismatch: {relative}")
        inputs.append((relative, source, pinned[relative]))
    if sha(hook) != HOOK_SHA:
        raise ValueError("Historical build hook hash mismatch")
    tools = historical / "tools"
    for name, expected in RUNNER_HASHES.items():
        if not (tools / name).is_file() or sha(tools / name) != expected:
            raise ValueError(f"Historical runner input hash mismatch: {name}")

    full = subprocess.check_output(
        ["git", "-C", str(repository), "rev-parse", "--verify", revision + "^{commit}"],
        text=True).strip()
    archive = subprocess.check_output(["git", "-C", str(repository), "archive", "--format=tar", full])
    source = output / "source"
    source.mkdir(parents=True)
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        tar.extractall(source, filter="data")
    for relative, original, _ in inputs:
        destination = contained(source, relative)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(original, destination)
    replacements = []
    for project, semantics in (("ShortCircuitComparison", "MeasureSemantics.Short.cs"),
                               ("HPeerComparison", "MeasureSemantics.H.cs")):
        directory = source / "benchmarks" / project
        program = directory / "Program.cs"
        program.rename(program.with_suffix(".cs.frozen"))
        shutil.copyfile(tools / "MeasureRunner.cs", program)
        shutil.copyfile(tools / semantics, directory / "MeasureSemantics.cs")
        replacements.append({"project": project, "runnerSha256": sha(program),
                             "semanticsSha256": sha(directory / "MeasureSemantics.cs")})
    destination = output / "harness/build-hook/V8BuildValidatedToolchain.cs"
    destination.parent.mkdir(parents=True)
    shutil.copyfile(hook, destination)
    result = {"sourceRevision": full, "historicalMeasuredRevision": manifest["measuredSourceHead"],
              "overlayManifestSha256": sha(overlay / "OVERLAY-MANIFEST.json"),
              "overlay": [{"path": relative, "sha256": digest} for relative, _, digest in inputs],
              "buildHookSha256": HOOK_SHA, "replacedAdmissionEntries": replacements,
              "measurementPerformed": False}
    (output / "STAGE.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {full}: {output}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--revision", required=True)
    parser.add_argument("--historical-root", type=Path, required=True)
    parser.add_argument("--build-hook", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    prepare(args.repository.resolve(), args.revision, args.historical_root.resolve(),
            args.build_hook.resolve(), args.output.resolve())


if __name__ == "__main__":
    main()
