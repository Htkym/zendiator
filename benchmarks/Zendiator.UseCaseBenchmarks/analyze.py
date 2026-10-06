"""Check the predeclared matrix against BDN JSON and export every observation."""

import csv
import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent
if len(sys.argv) != 2:
    raise SystemExit("Usage: python analyze.py OUTPUT_ROOT")
OUTPUT = Path(sys.argv[1]).resolve()
FIELDS = ("Type", "Method", "Lifetime", "Count", "Asynchronous")


def key(case):
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
for group in ("send", "features", "streams"):
    matrix = json.loads((ROOT / f"{group}.json").read_text(encoding="utf-8"))
    for case in matrix:
        identifier = key(case)
        if identifier in expected:
            raise ValueError(f"Duplicate predeclared case: {identifier}")
        expected[identifier] = group
    result_dir = OUTPUT / "runs" / group / "results"
    if not result_dir.exists():
        continue
    for path in result_dir.glob("*-full.json"):
        data = json.loads(path.read_text(encoding="utf-8"))
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
with (OUTPUT / "all-results.csv").open("w", encoding="utf-8", newline="") as output:
    writer = csv.DictWriter(output, fieldnames=("Group", *FIELDS, "MeanNs", "MedianNs", "N", "StdDevNs", "AllocatedBytes"))
    writer.writeheader()
    writer.writerows(rows)

for group in ("send", "features", "streams"):
    wanted = {identifier for identifier, value in expected.items() if value == group}
    found = {identifier for identifier, value in actual.items() if value == group}
    print(f"{group}: {len(found)}/{len(wanted)} cases")
    if (OUTPUT / "runs" / group / "outcome.json").exists() and wanted != found:
        raise ValueError(f"Missing {group}: {wanted - found}")
