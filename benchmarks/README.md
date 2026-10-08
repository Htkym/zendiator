# Use-case benchmarks

[日本語版](README.ja.md)

The checked-in fixture compares Zendiator with Immediate.Handlers 4.2.0, DispatchR.Mediator 2.3.1, Mediator 3.0.2 and MediatR 14.2.0. It uses each library's documented entry and registration. The full matrix contains Send 125, features 55, streams 120 and relay 60 cases, across 74 comparison keys. Unsupported combinations remain unmeasured.

## Read the comparison

The [README](../README.md#performance) shows eight balanced examples from the 2026-10-08 full run at source `1c41d2b105073d2dc9be2c0e8684fdce2fa11f55`. [Selected unrounded data](results/20261008-1c41d2b-summary.csv) includes Mean, confidence-interval Error, StdDev, standard error, retained N and managed allocated bytes.

That run used Windows 11 x64, Intel Core Ultra 7 258V, SDK 10.0.401, .NET 10.0.12, DI 10.0.12, BenchmarkDotNet 0.15.8, Release, affinity mask 1, 20 warmups, 12 measurements, requested 500 ms iterations and one launch per case. Libraries were rotated within comparison-key order using the interleaved driver. The 394-case correctness gate and each saved consumer's artifact gate passed. BDN retained 9–12 statistical observations per case after overhead adjustment and outlier handling.

The cases distinguish operations with different boundaries:

| Case | Timed operation |
|---|---|
| Typed | Send through an already resolved entry |
| ResolveSend | Resolve the entry again in the same scope, then Send |
| ScopeK1 / ScopeK10 | Create a scope, first resolve, send 1 / 10 times, dispose |
| ScopeOnly / ScopeResolve | Scope creation/disposal, without / with first entry resolution |
| FirstSend | First entry resolution and Send; scope creation/disposal is outside the measurement |
| Stream | Creation or the specified consumption, including its disposal |
| Relay | Synchronous preprocessing that directly returns the next enumerable |

FirstSend uses 16384 pre-created scopes per measured iteration, unroll factor 1. Stream totals are whole-operation values, not per-item costs. Handlers are lightweight; Notification and Stream include suspended cases, but Send handlers complete synchronously. API shapes and DI registrations differ. One launch does not establish independent-run reproducibility, application startup or HTTP/I/O latency, a causal before/after gain, or a universal ranking.

## Reproduce

Use .NET 10, PowerShell and Python 3. Package versions are locked. From the repository root:

```powershell
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group smoke
```

`-Group all` runs the full matrix in library-type blocks. It uses the same BDN settings but has a different execution order from the comparison above. Logs and full BDN JSON are written to a fresh ignored `.local/benchmarks/<timestamp>` directory; an existing output directory is not overwritten.

For comparison-key rotation, run from a clean committed checkout with SDK 10.0.401 and keep the output outside that checkout:

```powershell
python benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py --group all --output D:/zendiator-results/new-run --dry-run
```

After reviewing the plan, omit `--dry-run` to execute. `--max-cases 2` makes a bounded partial run; continue it with the same output path and `--resume`, omitting `--max-cases`. Resume requires the same checkout, commit, SDK and matrix and valid saved artifact proofs. Do not run benchmarks concurrently with builds or other measurement workloads. Nested checkouts can make BDN find more than one project; use an isolated checkout.

The generated fixture and matrix are in [Zendiator.UseCaseBenchmarks](Zendiator.UseCaseBenchmarks). Regenerate with `Generate.ps1` / `build_case_lists.py` only when deliberately changing cases, then review the diff and correctness gate. `Run-Jit.ps1` and `--breakdown` provide separate code/allocation diagnostics; their output is not competitive timing evidence.
