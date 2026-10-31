# Use-case benchmarks

[日本語版](README.ja.md)

The checked-in fixture compares Zendiator with Immediate.Handlers 4.2.0, DispatchR.Mediator 2.3.1, Mediator 3.0.2 and MediatR 14.2.0. It uses each library's documented entry and registration. The full matrix contains Send 125, features 55, streams 120 and relay 60 cases, across 74 comparison keys. Unsupported combinations remain unmeasured.

## Read the comparison

The [historical results below](#historical-040-results) show eight balanced examples from the 2026-10-08 full run at source `1c41d2b105073d2dc9be2c0e8684fdce2fa11f55`. [Selected unrounded data](results/20261008-1c41d2b-summary.csv) includes Mean, confidence-interval Error, StdDev, standard error, retained N and managed allocated bytes.

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

Commands run the fixtures at the source revision you have checked out. To reproduce
the historical table, use its pinned `1c41d2b` source and matching dependencies;
running the current 0.5.0 checkout does not reproduce that historical implementation.
For Zendiator, Send and non-notification feature cases use a canonical composition;
Stream and Relay use native cursor sources. Notification retains a separate legacy
registration. These fixtures do not supply a new measurement of the final 0.5.0 candidate.

Use .NET 10, PowerShell and Python 3. Package versions are locked. From the
repository root:

```powershell
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group smoke
```

`-Group all` runs the full checked-in matrix in library-type blocks, which differs
from the comparison-key rotation used for the historical table. Raw BenchmarkDotNet
JSON and logs accompany the results. The measured source, dependencies, workload and
execution order are part of a comparison; the command alone does not establish
matching conditions or a current-release ranking.

## Stream comparison boundaries

Current Zendiator Stream fixtures use named typed cursors and a canonical
composition; competitors use their own producer and registration shapes.
Full/EarlyBreak use standard enumeration through StreamAsync. FullTyped and
EarlyBreakTyped use StreamSource(...).ConsumeAsync(...) with a class sink.
The typed plan is not IAsyncEnumerable and creates a private owner per consumption.
EarlyBreak16 consumes one item from a 16-item input, not 16 items.

Standard enumeration, typed terminals and HTTP are separate evaluations. A native
cursor and a compiler-generated iterator have different production costs, so these
are complete product-path comparisons, not isolated dispatcher timings. A protocol
change or a lower allocation count alone does not establish a speedup; typed
consumption does not guarantee lower time or allocation.
## Historical 0.4.0 results

These results belong to the historical source below, not the current 0.5.0 candidate.

These selected observations help distinguish steady-state dispatch from first use and stream consumption. Lower mean time and lower managed allocation are separate benefits.

|Operation|Zendiator Mean ns|B/op|Peer|Mean ns|B/op|
|---|---:|---:|---|---:|---:|
|Resolve + Send in an existing scope; 5 behaviors|32.25|0|Immediate|46.07|0|
|New scope + first resolve + Send + dispose; 0 behaviors|79.43|376|Immediate|96.47|368|
|New scope + first resolve + Send + dispose; 5 behaviors|169.08|568|Immediate|121.61|568|
|First resolve + Send in a pre-created scope; 5 behaviors|807.98|440|Immediate|605.65|440|
|Synchronous notification; 16 subscribers|121.77|0|DispatchR|175.40|0|
|Synchronous stream; all 1024 items; 0 behaviors|14,190.35|216|Immediate|14,222.54|144|
|Synchronous stream; early break from a 16-item input; 0 behaviors|62.57|216|Immediate|42.84|144|
|Synchronous relay; all 1024 items; 5 preprocessing stages|15,028.75|216|DispatchR|13,610.87|144|

The existing-scope Send above has a lower observed mean than Immediate, while first resolve with behaviors and the one-send new-scope workload have higher means. The 1024-item Stream0 time difference is only about 0.23%; it does not establish a ranking, and Zendiator allocates 72 B more. Partial stream consumption and relay remain unfavorable in these examples.

Source snapshot `1c41d2b105073d2dc9be2c0e8684fdce2fa11f55`, measured 2026-10-08: Windows 11 x64, Intel Core Ultra 7 258V, SDK 10.0.401, .NET 10.0.12, BenchmarkDotNet 0.15.8, Release, affinity mask 1, 20 warmups, 12 measurements, requested 500 ms iterations, 1 launch per case. Peers shown are Immediate.Handlers 4.2.0 and DispatchR.Mediator 2.3.1; DI is 10.0.12.

Means are after BDN overhead adjustment and outlier handling; retained N is 9–12. The [unrounded selected data](results/20261008-1c41d2b-summary.csv) separates Mean, confidence-interval Error, StdDev, N and allocation. The full run covered 360 cases across 74 comparison keys. One launch does not establish independent-run reproducibility, universal non-regression, or a controlled before/after improvement.

Handlers are deliberately lightweight. API shapes and DI registrations differ between libraries. ScopeK1 includes creation and disposal; FirstSend excludes them. Stream/relay figures include the complete benchmark operation, not one item. The relay behaviors synchronously preprocess and directly return the next enumerable. These results do not predict application startup, HTTP latency or asynchronous I/O. See the [benchmark instructions above](#reproduce) for the fixture and reproduction conditions.
