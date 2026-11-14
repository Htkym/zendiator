"""Project boundaries and semantic gates for one shared measurement driver."""

from pathlib import Path


SUITES = {
    "usecase": ("Zendiator.UseCaseBenchmarks", 389),
    "hooks": ("HPeerComparison", 18),
    "short": ("ShortCircuitComparison", 6),
    "foundatio": ("FoundatioReleaseComparison", 16),
}
BENCHMARKS = Path(__file__).resolve().parent.parent

PERF03_DIAGNOSTIC_FILES = (
    "Perf03CauseVariant.cs", "Perf03DiagnosticControl.cs", "StreamCursorState.cs", "snapshot-provenance.json",
    "Zendiator.StreamCanonical.DF7872ACF770B8DDDA6D6A61054B21A2251A08597B558387231219CCDDFB0B4B.g.cs",
    "Zendiator.StreamCanonical.B80A3F15A7016AF93086943F607702393A9513056C3C751D172215C32167550B.g.cs",
    "originals/StreamCursorState.cs.txt",
    "originals/Zendiator.StreamCanonical.DF7872ACF770B8DDDA6D6A61054B21A2251A08597B558387231219CCDDFB0B4B.g.cs.txt",
    "originals/Zendiator.StreamCanonical.B80A3F15A7016AF93086943F607702393A9513056C3C751D172215C32167550B.g.cs.txt",
)


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
    names = ("ArtifactGate.cs", "ArtifactEvidence.cs", "ChildEvidence.cs", "perf03_counted_map.py")
    if project_suite(project) == "usecase":
        return {name: project / name for name in (*names, "Program.cs")}
    return {**{name: shared / name for name in names},
            "BenchmarkGate.cs": project / "BenchmarkGate.cs",
            "MeasurementProgram.cs": BENCHMARKS / "Measurement/MeasurementProgram.cs",
            "RuntimeEvidence.cs": BENCHMARKS / "Measurement/RuntimeEvidence.cs"}


def perf03_implementation_files(project):
    project = Path(project)
    root = project.parent.parent
    diagnostics = root / "src/Perf03CauseDiagnostics"
    files = {f"benchmarks/{project.name}/{name}": project / name
             for name in ("Perf03CauseGate.cs", "Perf03CauseStreams.cs")}
    files.update({path.relative_to(root).as_posix(): path for path in sorted(diagnostics.rglob("*")) if path.is_file()})
    if any("src/Perf03CauseDiagnostics/" + name not in files for name in PERF03_DIAGNOSTIC_FILES):
        raise ValueError("PERF03 isolated control/snapshot/provenance sources are missing")
    return files
