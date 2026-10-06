# Use-case benchmarks

[日本語版](README.ja.md)

`Zendiator.UseCaseBenchmarks` is the standard comparison for the current implementation. It fixes five library versions and compares the operations through their documented entries: Send with 0/1/3/5 Behaviors, Void and generic requests, Notification with 0/1/4/16 subscribers, and Stream creation, complete or partial enumeration, early exit, and cancellation. Its generated fixture source is checked in so the cases are reviewable.

From the repository root, with .NET 10, PowerShell, and Python 3 available:

```powershell
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group smoke
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group all
```

The smoke run checks one new-scope Send and one suspended Notification. The full run checks 125 Send, 55 feature, 120 Stream, and 60 relay-Behavior Stream cases. It restores locked packages, builds Release, runs the 394-case correctness gate before each group, then uses BenchmarkDotNet with CPU affinity 1, 20 warmup iterations, 12 measurement iterations, a requested 500 ms iteration time, and one launch per case. Each case has a separate child process. BenchmarkDotNet 0.15.8 runs the benchmark types in blocks; each type here represents one library. This run does not interleave the libraries of one case or rotate the first library. Consider time drift when interpreting small differences. A future interleaved run needs a parent driver that invokes one selected case/library at a time in rotated comparison-key order while retaining the correctness, child-count, source, and DLL-hash checks. The runner checks case and child counts, failures, source stability, and one consistent Zendiator DLL hash across all children.

Besides Send through a resolved entry (`Typed`), re-resolution in the same scope (`ResolveSend`), and one or ten sends in a new scope (`ScopeK1`, `ScopeK10`), the Send group splits first resolution in a new scope into stages. `ScopeOnly` creates and disposes a scope, and `ScopeResolve` adds the first entry resolution. `FirstSend` measures only the first entry resolution and Send; each invocation takes one scope created by the iteration setup (16384 invocations, unroll factor 1, and the cleanup checks that every scope was used). None of these cases covers an HTTP or MVC pipeline. The `relay` group is a separate case in which every library's Stream Behavior pre-processes and returns next's sequence without an async iterator. Libraries differ in when that pre-processing runs and where its exception surfaces, so the correctness gate records both in `correctness.json`.

BenchmarkDotNet is pinned to 0.15.8 because `FirstSend` recognizes the single-operation JIT preparation call by its `EngineFactory.Jit` stack frame. The check fails closed if that frame is absent; measured iterations must each consume all 16384 scopes. The runner also verifies 12 `WorkloadActual` rows with 16384 operations for every `FirstSend` case.

Every run gets a new ignored `.local/benchmarks/<timestamp>` directory. `run.log`, full BDN JSON, child assembly hashes, source revision and digest, SDK version, and failure logs remain there. A successful full run also writes `all-results.csv` with mean, median, standard deviation, iteration count, and allocated bytes, plus `RESULT.ja.md` with every comparable case. `-Group send`, `features`, `streams`, or `relay` runs one group and writes its CSV. `-OutputRoot` selects a new output directory; an existing directory is never overwritten. Keep failed runs and record the reason for exclusion.

To regenerate the fixture or matrix after changing a case, run `Generate.ps1` or `build_case_lists.py` from `Zendiator.UseCaseBenchmarks`, review the resulting source/JSON diff, and rerun correctness checks.

For machine-code inspection, run `pwsh -NoProfile -File benchmarks/Run-Jit.ps1`. It warms the Send0/Send5 new-scope paths and requires Tier1 output for the requested JIT pattern. Use `-Pattern` to inspect another method and retain the disassembly beside the BDN data. Machine-code size alone does not establish a speed improvement.

For allocation diagnostics after a Release build, run `dotnet ./bin/Release/net10.0/Zendiator.UseCaseBenchmarks.dll --breakdown` from `benchmarks/Zendiator.UseCaseBenchmarks`, with `COLD_RUN` set to the absolute path of a new output directory. `allocation-breakdown.json` separates scope creation, entry resolution, Behavior resolution, and Send. `resolver-scale.json` covers 1/2/6/32/64/128 dependency types in forward and reverse order. The reflection-based scale probe and simple timer values are not competitive speed measurements. `--stream-breakdown` measures the bytes of one complete enumeration for Zendiator and Immediate Stream0, Stream5, and five relay Behaviors (16 and 1024 items, synchronous and suspended), apportions them to types by GC allocation-tick samples, and writes `stream-allocations.json`. The per-type values are sampled estimates.

The matrix uses lightweight synchronously completing Send/Void handlers and precreated requests. It includes actual suspension for Notification and Stream, but not suspended Send handlers, provider construction, Send exception timing, or Native AOT speed. Immediate uses a request-specific generated entry; Zendiator keeps `IZendiator.SendAsync`. An unmatched case is reported as not measured. One launch describes the measured run, not an overall-fastest ranking.

### Cross-library execution driver

`Run-Comparison.ps1` remains the type-block runner used for the historical 360-case result. `Zendiator.UseCaseBenchmarks/run_interleaved.py` is a separate driver: it uses `interleaved_plan.py` to rotate the first library for each comparison key, then launches exactly one BDN case per invocation and verifies the concatenated child logs against that plan. It requires a clean committed worktree, SDK 10.0.401, locked restore, a Release build, and a new output directory. Each group runs the 394-case correctness gate once. The driver retains failed attempts, verifies source/SDK/parent and child DLL identities, checks one result and child per case, and resumes completed cases without rerunning them. The old type-block output is never imported.

From the repository root, plan without building or launching BDN:

```powershell
python benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py --group all --output D:\gitroot\zendiator\.local\benchmarks\new-interleaved-run --dry-run
```

In a quiet measurement slot, use `--max-cases 2` for a bounded two-case smoke. This leaves a partial run without a final outcome or report. After checking its evidence, continue with the same output path and `--resume`, omitting `--max-cases`. A full run omits both flags. The driver has only been checked with dry-run so far; do not label a run interleaved until its case logs, BDN JSON, and child evidence pass final verification.
