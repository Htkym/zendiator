"""Plan and audit a future per-case BDN run across benchmark types.

BenchmarkDotNet's orderer is scoped to one benchmark type. Each type in this
fixture represents one library, so a separate parent driver must invoke the
single cases in this plan to achieve cross-library execution order.
"""

import argparse
import json
import re
from collections import OrderedDict
from pathlib import Path

from artifact_gate import filesystem_path, read


LIBRARIES = ("Zendiator", "MediatRHistorical", "Mediator", "DispatchR", "Immediate", "Foundatio")
FIELDS = ("Type", "Method", "Lifetime", "Count", "Asynchronous", "Project")
PROJECTS = ("usecase", "hooks", "short", "foundatio")
HOOK_TYPES = {f"H{name}Benchmarks": name for name in LIBRARIES}
SHORT_TYPES = {f"{name}ShortCircuit": name for name in LIBRARIES if name != "MediatRHistorical"}
SHORT_TYPES["MediatRShortCircuit"] = "MediatRHistorical"
BENCHMARK = re.compile(r"^// Benchmark: ([^.]+)\.([^:]+):.*?(?: \[([^]]+)\])?$")


def library(case):
    name = normalize_case(case)["Type"]
    if name in HOOK_TYPES:
        return HOOK_TYPES[name]
    if name in SHORT_TYPES:
        return SHORT_TYPES[name]
    matches = [prefix for prefix in LIBRARIES if name.startswith(prefix)]
    if len(matches) != 1:
        raise ValueError(f"Cannot identify one library for {case!r}")
    return matches[0]


def comparison_key(case):
    case = normalize_case(case)
    name = case["Type"]
    family = "Hooks" if name in HOOK_TYPES else "ShortCircuit" if name in SHORT_TYPES else name[len(library(case)):]
    return (
        family,
        case["Method"],
        case.get("Lifetime"),
        case.get("Count"),
        case.get("Asynchronous"),
    )


def normalize_case(case):
    if not isinstance(case, dict) or any(not isinstance(case.get(field), str) or not case[field]
                                         for field in ("Type", "Method")):
        raise ValueError("Matrix cases require nonempty Type and Method strings")
    if set(case) - set(FIELDS):
        raise ValueError(f"Unknown matrix case fields: {sorted(set(case) - set(FIELDS))}")
    project = case.get("Project")
    if project is not None and project not in PROJECTS:
        raise ValueError(f"Unknown benchmark Project: {project!r}")
    name = case["Type"]
    expected = ("hooks" if name in HOOK_TYPES else "short" if name in SHORT_TYPES
                else "foundatio" if name.startswith("Foundatio") else "usecase")
    if (project or "usecase") != expected:
        raise ValueError(f"Benchmark type {name!r} requires Project {expected!r}")
    # C# MatrixCase treats omitted and explicit-null optional parameters identically.
    return {name: value for name, value in case.items()
            if (name not in FIELDS[2:] or value is not None) and not (name == "Project" and value == "usecase")}


def identity(case):
    case = normalize_case(case)
    return tuple(case.get(field) for field in FIELDS)


def make_plan(cases):
    cases = [normalize_case(case) for case in cases]
    if len({identity(case) for case in cases}) != len(cases):
        raise ValueError("Matrix contains duplicate cases")
    groups = OrderedDict()
    for case in cases:
        groups.setdefault(comparison_key(case), []).append(case)

    plan = []
    for ordinal, (key, members) in enumerate(groups.items()):
        members.sort(key=lambda case: LIBRARIES.index(library(case)))
        names = [library(case) for case in members]
        if len(names) != len(set(names)):
            raise ValueError(f"Duplicate library in comparison key {key!r}")
        first = ordinal % len(members)
        for case in members[first:] + members[:first]:
            plan.append({"ComparisonKey": list(key), "Library": library(case), "Case": case})

    if len(plan) != len(cases) or {identity(row["Case"]) for row in plan} != {identity(case) for case in cases}:
        raise ValueError("Planned cases differ from the matrix")
    return plan, len(groups)


def make_ab_plan(cases, reverse=False):
    plan, _ = make_plan(cases)
    if not plan:
        raise ValueError("A/B matrix cannot be empty")
    schedule = []
    previous, block, member = None, -1, 0
    for index, row in enumerate(plan, 1):
        key = tuple(row["ComparisonKey"])
        if key != previous:
            previous, block, member = key, block + 1, 0
        sides = ("baseline", "candidate") if (block + member) % 2 == 0 else ("candidate", "baseline")
        schedule.extend(dict(row, Side=side, CaseIndex=index) for side in sides)
        member += 1
    if reverse:
        schedule.reverse()
        schedule = [dict(row, CaseIndex=len(plan) + 1 - row["CaseIndex"]) for row in schedule]
    return [dict(row, Ordinal=index) for index, row in enumerate(schedule, 1)]


def logged_cases(path):
    result = []
    for line in filesystem_path(path).read_text(encoding="utf-8").splitlines():
        match = BENCHMARK.match(line)
        if not match:
            continue
        case = {"Type": match.group(1), "Method": match.group(2)}
        for item in (match.group(3) or "").split(", "):
            if "=" not in item:
                continue
            name, value = item.split("=", 1)
            if name == "Count":
                case[name] = int(value)
            elif name == "Asynchronous":
                case[name] = value == "True"
            elif name == "Lifetime":
                case[name] = value
        result.append(case)
    return result


def verify_log(plan, path):
    actual = logged_cases(path)
    expected = [row["Case"] for row in plan]
    if len(actual) != len(expected):
        raise ValueError(f"Log has {len(actual)} cases; plan has {len(expected)}")
    for ordinal, (want, got) in enumerate(zip(expected, actual), 1):
        # BDN identifies the type and parameters; the selected project is a parent-driver control.
        want = normalize_case(want)
        if "Project" in want:
            got["Project"] = want["Project"]
        if identity(want) != identity(got):
            raise ValueError(f"Execution order mismatch at {ordinal}: expected {want!r}; logged {got!r}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("matrix", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--verify-log", type=Path)
    args = parser.parse_args()

    cases = read(args.matrix)
    plan, keys = make_plan(cases)
    if args.output:
        filesystem_path(args.output).write_text(json.dumps(plan, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.verify_log:
        verify_log(plan, args.verify_log)
    print(f"{len(plan)} cases across {keys} comparison keys; first libraries: " +
          ", ".join(row["Library"] for row in plan[: min(len(plan), 10)]))


if __name__ == "__main__":
    main()
