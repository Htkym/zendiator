"""Compare baseline generation and fixed WP04 workload bodies without executing code."""

import argparse
import hashlib
import json
import os
import re
from pathlib import Path


GENERATED = Path("benchmarks/Zendiator.UseCaseBenchmarks/obj/Release/net10.0/generated")
PAIRS = (
    ("ZendiatorStream0", "ZendiatorStructUpstreamStream0"),
    ("ZendiatorStream5", "ZendiatorStructUpstreamStream5"),
    ("ZendiatorRelay5", "ZendiatorStructUpstreamRelay5"),
)
FIXED_INPUTS = (
    "benchmarks/Zendiator.UseCaseBenchmarks/GeneratedCases.cs",
    "benchmarks/Zendiator.UseCaseBenchmarks/Work.cs",
    "benchmarks/Zendiator.UseCaseBenchmarks/Zendiator.UseCaseBenchmarks.csproj",
    "benchmarks/Zendiator.UseCaseBenchmarks/packages.lock.json",
)


def filesystem_path(value):
    value = os.path.abspath(value)
    if os.name == "nt" and not value.startswith("\\\\?\\"):
        value = "\\\\?\\" + value
    return Path(value)


def digest(value):
    return hashlib.sha256(value).hexdigest()


def class_block(source, name):
    match = re.search(r"\[MemoryDiagnoser\]\s*public class " + name + r"\b", source)
    if not match:
        raise ValueError(f"Missing benchmark class {name}")
    opening = source.index("{", match.start())
    return source[match.start():block_end(source, opening)]


def block_end(source, opening):
    # The fixed benchmark/generated-shim grammar has no braces in string literals.
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return end


def shim_block(raw, ordinal):
    source = raw.decode("utf-8")
    method = source.index(f" Shim{ordinal}<")
    attribute = source.rfind("        [global::System.Runtime.CompilerServices.InterceptsLocation", 0, method)
    opening = source.index("{", method)
    if attribute < 0:
        raise ValueError(f"Missing attribute for Shim{ordinal}")
    return source[attribute:block_end(source, opening)].encode("utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline-root", required=True)
    parser.add_argument("--candidate-root", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    baseline, candidate = map(filesystem_path, (args.baseline_root, args.candidate_root))
    if not (baseline / GENERATED).is_dir() or not (candidate / GENERATED).is_dir():
        raise ValueError("Both generated source directories must already exist; this script does not build.")
    before = {str(p.relative_to(baseline / GENERATED)): p for p in (baseline / GENERATED).rglob("*.cs")}
    after = {str(p.relative_to(candidate / GENERATED)): p for p in (candidate / GENERATED).rglob("*.cs")}
    common = sorted(before.keys() & after.keys())
    rows = []
    for name in common:
        old, new = before[name].read_bytes(), after[name].read_bytes()
        rows.append({"path": name, "baseline_sha256": digest(old), "candidate_sha256": digest(new), "byte_identical": old == new})
    changed = [row["path"] for row in rows if not row["byte_identical"]]
    shared = [name for name in changed if Path(name).name == "Zendiator.Canonical.Multiple.Interceptors.g.cs"]
    append_only, shim_rows, shared_proof = False, [], None
    if len(shared) == 1:
        old, new = before[shared[0]].read_bytes(), after[shared[0]].read_bytes()
        closing_index = old.rfind(b"    }")
        if closing_index < 0:
            raise ValueError("Missing generated class closing delimiter")
        prefix, closing = old[:closing_index], old[closing_index:]
        inserted = new[len(prefix):-len(closing)]
        append_only = (new.startswith(prefix) and new.endswith(closing)
                       and b"Competitive.BenchmarkStructUpstreamStreams" in inserted
                       and b"Shim3<TComposition>" in inserted)
        for ordinal in range(3):
            old_shim, new_shim = shim_block(old, ordinal), shim_block(new, ordinal)
            shim_rows.append({"shim": f"Shim{ordinal}", "bytes": len(old_shim),
                              "baseline_sha256": digest(old_shim), "candidate_sha256": digest(new_shim),
                              "byte_identical": old_shim == new_shim,
                              "newline_normalized_identical": old_shim.replace(b"\r\n", b"\n") == new_shim.replace(b"\r\n", b"\n")})
        shared_proof = {"prefix_bytes": len(prefix), "baseline_prefix_sha256": digest(prefix),
                        "candidate_prefix_sha256": digest(new[:len(prefix)]), "closing_bytes_hex": closing.hex(),
                        "closing_byte_identical": new.endswith(closing), "inserted_bytes": len(inserted),
                        "inserted_sha256": digest(inserted), "old_shims": shim_rows}
        append_only = append_only and all(row["byte_identical"] for row in shim_rows)
    original = (baseline / FIXED_INPUTS[0]).read_text()
    opt_in = (candidate / "benchmarks/Zendiator.UseCaseBenchmarks/StructUpstreamStreams.cs").read_text()
    pairs = []
    for old_name, new_name in PAIRS:
        restored = (class_block(opt_in, new_name).replace(new_name, old_name)
                    .replace("StructUpstreamRegistration.Create()", "StreamRegistration.Create()")
                    .replace("Competitive.Generated.BenchmarkStructUpstreamStreams.IZendiator", "Competitive.Generated.BenchmarkStreams.IZendiator"))
        pairs.append({"ordinary": old_name, "opt_in": new_name,
                      "same_body_except_whitespace_names_registration": re.sub(r"\s+", "", class_block(original, old_name)) == re.sub(r"\s+", "", restored)})
    fixed = [{"path": name, "byte_identical": (baseline / name).read_bytes() == (candidate / name).read_bytes(),
              "sha256": digest((candidate / name).read_bytes())} for name in FIXED_INPUTS]
    missing = sorted(before.keys() - after.keys())
    passed = (not missing and (not changed or len(changed) == 1 and append_only)
              and all(row["byte_identical"] for row in fixed)
              and all(row["same_body_except_whitespace_names_registration"] for row in pairs))
    report = {"baseline_commit": "7c69f2a7408a68f36cf25abc3c6ac74ef00c5054", "baseline_generated_files": len(before),
              "candidate_generated_files": len(after), "common_generated_files": len(common),
              "changed_common_files": changed, "shared_interceptor_only_adds_new_composition": append_only,
              "shared_interceptor_byte_proof": shared_proof, "missing_files": missing,
              "added_files": sorted(after.keys() - before.keys()), "fixed_inputs": fixed,
              "benchmark_body_pairs": pairs, "files": rows, "passed": passed}
    output = filesystem_path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key not in ("files", "added_files")}, indent=2))
    print("Evidence:", output)
    raise SystemExit(0 if passed else 1)


if __name__ == "__main__":
    main()
