"""Check the predeclared matrix against BDN JSON and export every observation."""

import csv
import re
import sys
from pathlib import Path

from artifact_gate import filesystem_path, resolve_path, glob_paths, read

from interleaved_plan import normalize_case


ROOT = Path(__file__).resolve().parent
if len(sys.argv) not in (2, 4) or (len(sys.argv) == 4 and sys.argv[2] != "--matrix"):
    raise SystemExit("Usage: python analyze.py OUTPUT_ROOT [--matrix FOCUSED_MATRIX]")
OUTPUT = resolve_path(sys.argv[1])
FIELDS = ("Type", "Method", "Lifetime", "Count", "Asynchronous")
MATRICES = ({"custom": resolve_path(sys.argv[3])} if len(sys.argv) == 4 else
            {group: ROOT / f"{group}.json" for group in ("send", "features", "streams", "relay")})


def key(case):
    case = normalize_case(case)
    return tuple(str(case.get(field, "")) for field in FIELDS)


def parameters(value):
    result = {}
    for item in re.split(r"[,\&]\s*", value) if value else ():
        name, text = item.split("=", 1)
        result[name] = text.strip('"')
    return result


expected = {}
actual = {}
rows = []
for group, matrix_path in MATRICES.items():
    matrix = read(matrix_path)
    for case in matrix:
        identifier = key(case)
        if identifier in expected:
            raise ValueError(f"Duplicate predeclared case: {identifier}")
        expected[identifier] = group
    result_dir = OUTPUT / "runs" / group / "results"
    if not filesystem_path(result_dir).exists():
        continue
    for path in glob_paths(result_dir, "*-full.json"):
        data = read(path)
        for benchmark in data["Benchmarks"]:
            case = {
                "Type": benchmark["Type"],
                "Method": benchmark["Method"],
                **parameters(benchmark.get("Parameters") or ""),
            }
            identifier = key(case)
            if identifier in actual:
                raise ValueError(f"Duplicate BDN result: {identifier}")
            if expected.get(identifier) != group:
                raise ValueError(f"Unexpected BDN result: {group}: {identifier}")
            stats = benchmark.get("Statistics") or {}
            memory = benchmark.get("Memory") or {}
            actual[identifier] = group
            rows.append(
                {
                    "Group": group,
                    **{field: case.get(field, "") for field in FIELDS},
                    "MeanNs": stats.get("Mean", ""),
                    "MedianNs": stats.get("Median", ""),
                    "N": stats.get("N", ""),
                    "StdDevNs": stats.get("StandardDeviation", ""),
                    "AllocatedBytes": memory.get("BytesAllocatedPerOperation", ""),
                }
            )

rows.sort(key=lambda row: (row["Group"], row["Type"], row["Method"], row["Lifetime"], str(row["Count"]), str(row["Asynchronous"])))
with filesystem_path(OUTPUT / "all-results.csv").open("w", encoding="utf-8", newline="") as output:
    writer = csv.DictWriter(output, fieldnames=("Group", *FIELDS, "MeanNs", "MedianNs", "N", "StdDevNs", "AllocatedBytes"))
    writer.writeheader()
    writer.writerows(rows)

for group in MATRICES:
    wanted = {identifier for identifier, value in expected.items() if value == group}
    found = {identifier for identifier, value in actual.items() if value == group}
    print(f"{group}: {len(found)}/{len(wanted)} cases")
    if filesystem_path(OUTPUT / "runs" / group / "outcome.json").exists() and wanted != found:
        raise ValueError(f"Missing {group}: {wanted - found}")
