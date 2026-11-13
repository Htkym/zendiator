"""Project boundaries and semantic gates for one shared measurement driver."""

from pathlib import Path


SUITES = {
    "usecase": ("Zendiator.UseCaseBenchmarks", 389),
    "hooks": ("HPeerComparison", 18),
    "short": ("ShortCircuitComparison", 6),
    "foundatio": ("FoundatioReleaseComparison", 16),
}
BENCHMARKS = Path(__file__).resolve().parent.parent


def suite_name(case):
    name = case.get("Project", "usecase")
    if name not in SUITES:
        raise ValueError(f"Unknown benchmark project: {name!r}")
    return name


def project_for(case):
    return BENCHMARKS / SUITES[suite_name(case)][0]


def project_suite(project):
    return next((name for name, (directory, _) in SUITES.items() if Path(project).name == directory), "usecase")


def consumer_name(project):
    return SUITES[project_suite(project)][0]


def gate_count(project):
    return SUITES[project_suite(project)][1]


def implementation_files(project):
    project = Path(project)
    shared = BENCHMARKS / "Zendiator.UseCaseBenchmarks"
    names = ("ArtifactGate.cs", "ArtifactEvidence.cs", "ChildEvidence.cs")
    if project_suite(project) == "usecase":
        return {name: project / name for name in (*names, "Program.cs")}
    return {**{name: shared / name for name in names},
            "BenchmarkGate.cs": project / "BenchmarkGate.cs",
            "MeasurementProgram.cs": BENCHMARKS / "Measurement/MeasurementProgram.cs",
            "RuntimeEvidence.cs": BENCHMARKS / "Measurement/RuntimeEvidence.cs"}
