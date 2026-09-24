# Use-case benchmarks

[日本語版](README.ja.md)

`Zendiator.UseCaseBenchmarks` is the standard comparison for the current implementation. It fixes five library versions and compares the operations through their documented entries: Send with 0/1/3/5 Behaviors, Void and generic requests, Notification with 0/1/4/16 subscribers, and Stream creation, complete or partial enumeration, early exit, and cancellation. Its generated fixture source is checked in so the cases are reviewable.

From the repository root, with .NET 10, PowerShell, and Python 3 available:

```powershell
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group smoke
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group all
```

The smoke run checks one new-scope Send and one suspended Notification. The full run checks 80 Send, 55 feature, and 120 Stream cases. It restores locked packages, builds Release, runs the 304-case correctness gate before each group, then uses BenchmarkDotNet with CPU affinity 1, 20 warmup iterations, 12 measurement iterations, a requested 500 ms iteration time, and one launch per case. Each case has a separate child process. The runner checks case and child counts, failures, source stability, and one consistent Zendiator DLL hash across all children.

Every run gets a new ignored `.local/benchmarks/<timestamp>` directory. `run.log`, full BDN JSON, child assembly hashes, source revision, and failure logs remain there. A successful full run also writes `all-results.csv` with mean, median, standard deviation, iteration count, and allocated bytes, plus `RESULT.ja.md` with every comparable case. `-Group send`, `features`, or `streams` runs one group and writes its CSV. `-OutputRoot` selects a new output directory; an existing directory is never overwritten. Keep failed runs and record the reason for exclusion.

To regenerate the fixture or matrix after changing a case, run `Generate.ps1` or `build_case_lists.py` from `Zendiator.UseCaseBenchmarks`, review the resulting source/JSON diff, and rerun correctness checks.

For machine-code inspection, run `pwsh -NoProfile -File benchmarks/Run-Jit.ps1`. It warms the Send0/Send5 new-scope paths and requires Tier1 output for the requested JIT pattern. Use `-Pattern` to inspect another method and retain the disassembly beside the BDN data. Machine-code size alone does not establish a speed improvement.

The matrix uses lightweight synchronously completing Send/Void handlers and precreated requests. It includes actual suspension for Notification and Stream, but not suspended Send handlers, provider construction, Send exception timing, or Native AOT speed. Immediate uses a request-specific generated entry; Zendiator keeps `IZendiator.SendAsync`. An unmatched case is reported as not measured. One launch describes the measured run, not an overall-fastest ranking.
