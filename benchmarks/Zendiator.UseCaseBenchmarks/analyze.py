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
FIELDS = ("Type", "Method", "Lifetime", "Count", "Asynchronous", "Project")
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
declared = {}
actual = {}
rows = []
for group, matrix_path in MATRICES.items():
    matrix = read(matrix_path)
    for case in matrix:
        case = normalize_case(case)
        identifier = key(case)
        if identifier in expected:
            raise ValueError(f"Duplicate predeclared case: {identifier}")
        expected[identifier] = group
        declared.setdefault((group, identifier[:-1]), []).append(case)

for group in MATRICES:
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
            if set(case) - set(FIELDS[:-1]):
                raise ValueError(f"Unexpected BDN parameters: {case}")
            timed_identity = tuple(str(case.get(field, "")) for field in FIELDS[:-1])
            matches = declared.get((group, timed_identity), [])
            if not matches:
                raise ValueError(f"Unexpected BDN result: {group}: {timed_identity}")
            if len(matches) != 1:
                raise ValueError(f"Ambiguous predeclared BDN result: {group}: {timed_identity}")
            # Project selects the parent process; BDN does not measure or authenticate it.
            if "Project" in matches[0]:
                case["Project"] = matches[0]["Project"]
            identifier = key(case)
            if identifier in actual:
                raise ValueError(f"Duplicate BDN result: {identifier}")
            if expected.get(identifier) != group:
                raise ValueError(f"Unexpected BDN result: {group}: {identifier}")
            stats = benchmark.get("Statistics") or {}
            confidence = stats.get("ConfidenceInterval") or {}
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
                    "ConfidenceLowerNs": confidence.get("Lower", ""),
                    "ConfidenceUpperNs": confidence.get("Upper", ""),
                    "ConfidenceMarginNs": confidence.get("Margin", ""),
                    "AllocatedBytes": memory.get("BytesAllocatedPerOperation", ""),
                }
            )

rows.sort(key=lambda row: (row["Group"], row["Project"], row["Type"], row["Method"], row["Lifetime"], str(row["Count"]), str(row["Asynchronous"])))
with filesystem_path(OUTPUT / "all-results.csv").open("w", encoding="utf-8", newline="") as output:
    writer = csv.DictWriter(output, fieldnames=("Group", *FIELDS, "MeanNs", "MedianNs", "N", "StdDevNs",
                                              "ConfidenceLowerNs", "ConfidenceUpperNs", "ConfidenceMarginNs", "AllocatedBytes"))
    writer.writeheader()
    writer.writerows(rows)

for group in MATRICES:
    wanted = {identifier for identifier, value in expected.items() if value == group}
    found = {identifier for identifier, value in actual.items() if value == group}
    print(f"{group}: {len(found)}/{len(wanted)} cases")
    if filesystem_path(OUTPUT / "runs" / group / "outcome.json").exists() and wanted != found:
        raise ValueError(f"Missing {group}: {wanted - found}")
