# 0.1.0 Formal Measurements

2026-09-10. The current 0.1.0 performance values use only the formal RC2 measurements:
20 Send routes and 34 Stream routes.
The product implementation has not changed since the RC2 baseline, so the values are
carried over without remeasurement.
Older RC1/Hardening-1 numbers are not used as current values.
All measured values are kept; there were no failures, reruns, or exclusions. Conditions are
BenchmarkDotNet 0.15.8, Release, Throughput,
MemoryDiagnoser, 3 launches, 3 warmups / 8 measurements each, `OutlierMode=DontRemove`.
The machine is Intel Core Ultra 7 258V, 8 cores, Windows 11 `10.0.26200.9278`, SDK `10.0.300`,
runtime `.NET 10.0.8`, X64 RyuJIT x86-64-v3. Run scripts and raw evidence (CSV/JSON/BDN logs,
including launch/pilot/overhead/warmup operation counts and times) are retained as release evidence.
The headline numbers are also in `docs/release/0.1.0-release-notes.md`. For limits, see
[known limitations](release/known-limitations.md).

## Summary (current claims)

Values apply only to the measured routes and environment. They are not latency
guarantees or competitor rankings.

| Route | Zendiator | Allocated |
|---|---|---:|
| Typed Send Scoped 0/1/3/5 behaviors | 21.4/50.8/90.6/140.5 ns | 0 B |
| Typed Send Singleton 0/1/3/5 behaviors | 5.1/12.4/24.0/46.0 ns | 0 B |
| NativeVoid | 22.0 ns | 0 B |
| Generic Request (incl. response creation) | 28.6 ns | 24 B |
| Scope K=1 (create + resolve + send + dispose) | 113.1 ns | 360 B |
| Scoped, mediator resolved per call | 54.3 ns | 0 B |
| Concrete generated-type call | 22.3 ns | 0 B |
| Stream sync, 0 behaviors, 1024 items | 17.622 μs | 232 B |
| Stream async, 0 behaviors, 1024 items | 648.388 μs | 113,959 B |
| Stream creation only | 4.407 ns | 40 B |

## Comparison

### Against Direct (same suite)

Direct is a hand-written struct-continuation baseline and does not include DI resolution.
Do not use near-zero Direct values as ratio denominators.

| Route | Zendiator | Direct | How to read |
|---|---|---|---|
| Stream sync, 0 behaviors, 1024 items | 17.622 μs / 232 B | 12.947 μs / 120 B | Delta +4.675 μs, +112 B/stream (flat across item counts) |
| Stream async, 0 behaviors, 1024 items | 648.388 μs / 113,959 B | 343.341 μs / 360 B | Delta +113,599 B = 110.94 B/item (`OPT-stream-async`) |
| Stream creation only | 4.407 ns / 40 B | 6.974 ns / 120 B | Zendiator defers DI resolution and inner-iterator creation to the first MoveNext. Not whole-dispatch speed |
| Generic Request | 28.6 ns / 24 B | 2.651 ns / 24 B | 24 B is response creation common to both sides |
| Send E02 Direct, 0 behaviors | — | 0.069 ns (`ZeroMeasurement`) | At the measurement floor; not a comparison denominator |

## Send formal measurements (20 routes)

2026-09-10. Measured 20 Send routes with BenchmarkDotNet against the current source
unified on standard DI.
Synchronously completing typed Sends with 0/1/3/5 behaviors averaged 21.4/50.8/90.6/140.5 ns
for Scoped and 5.1/12.4/24.0/46.0 ns for Singleton. Allocation on these 8 routes was 0 B/op.
These values are limited to the measured environment and inputs; they are not grounds
for reverting the DI correctness fix.

### Conditions and reproduction

- Source: product implementation at commit `a1033e9a521b2c22c50b17a0c120fded42093be7`. For version and provenance, see [0.1.0 release notes](release/0.1.0-release-notes.md).
- Environment: Intel Core Ultra 7 258V, 8 cores, Windows 11 `10.0.26200.9278`, SDK `10.0.300`, runtime `.NET 10.0.8`, X64 RyuJIT x86-64-v3, Workstation GC.
- BenchmarkDotNet `0.15.8`, Release, Throughput, MemoryDiagnoser, 3 launches, 3 warmups / 8 measurements each, `OutlierMode=DontRemove`.
- BDN chooses the operation count per launch via pilot. All 24 measured values per route are kept, each in a separate child process. No ShortRun.
- Ran 16 main routes, 4 feature routes, then Stream. No other build/test/AOT ran during measurement, only doc organization in parallel. OS and background load were not fully pinned.
- Duration: main 10m45s, features 3m42s. Both processes exited 0. No failures, reruns, or excluded values.

Run/aggregation scripts and raw evidence (CSV/JSON/BDN logs) are retained as release evidence;
all 24 values per route are kept.
The JSON keeps not just the 24 results but launch, pilot, overhead, and warmup operation counts and times.
Table values aggregate mean, median, standard deviation, min/max, quartiles, and GC.

The normal build/test environment was SDK 10.0.400 / runtime 10.0.11, but on the permitted host
PATH resolves to the user-local .NET. This table uses 10.0.300 / 10.0.8 as reported by BDN itself.

### What is measured

Typed requests are structs holding value 41; handlers return 42. Behaviors increment a counter once and continue.
E02 Direct prepares the same handler and Behavior instances and calls them through a hand-written
struct continuation as the comparison baseline.
Direct includes no DI resolution. Values and Behavior counts are covered by the existing Benchmark.Tests.

Typed Sends use an acquired mediator and a warm scope, including per-node DI resolution in the measurement.
Scoped/Singleton registration uses the existing class-mode `AddZendiator`.
The concrete route declares the field with the actual generated concrete type, distinct from
the older interface-typed measurement.

NativeVoid completes a do-nothing handler; generic requests return a new `ZDto` holding value 41.
Direct and Zendiator outputs were matched before measuring. The generic 24 B includes response
creation common to both sides.

### Results

Time is ns per operation, allocation is B/op. The Direct column includes the measurement floor,
so no ratios are computed.
Medians and deviations are statistics over 24 iteration means, not p95/p99 per-request latency.

| Route | Mean ns | Median ns | StdDev ns | B/op |
|---|---:|---:|---:|---:|
| Scoped 0 behaviors | 21.432 | 21.353 | 0.201 | 0 |
| Scoped 1 behavior | 50.835 | 50.822 | 0.512 | 0 |
| Scoped 3 behaviors | 90.631 | 90.227 | 1.590 | 0 |
| Scoped 5 behaviors | 140.493 | 140.655 | 2.629 | 0 |
| Singleton 0 behaviors | 5.062 | 5.065 | 0.065 | 0 |
| Singleton 1 behavior | 12.442 | 12.405 | 0.135 | 0 |
| Singleton 3 behaviors | 24.033 | 22.815 | 2.228 | 0 |
| Singleton 5 behaviors | 46.000 | 44.676 | 6.065 | 0 |
| E02 Direct 0 behaviors | 0.069 | 0.057 | 0.059 | 0 |
| E02 Direct 1 behavior | 3.703 | 3.663 | 0.150 | 0 |
| E02 Direct 3 behaviors | 11.846 | 11.746 | 0.689 | 0 |
| E02 Direct 5 behaviors | 19.760 | 19.342 | 1.005 | 0 |
| Scoped concrete 0 behaviors | 22.257 | 21.949 | 0.571 | 0 |
| NativeVoid | 21.965 | 21.642 | 0.580 | 0 |
| Direct NativeVoid | 0.004 | 0.002 | 0.006 | 0 |
| Generic request | 28.641 | 28.563 | 0.694 | 24 |
| Direct generic request | 2.651 | 2.636 | 0.074 | 24 |
| Scoped full scope K=1 | 113.106 | 112.269 | 4.768 | 360 |
| Scoped mediator resolved from the same scope per send | 54.269 | 52.451 | 4.575 | 0 |
| Singleton mediator resolved per send | 13.984 | 14.285 | 0.645 | 0 |

Full-scope rows include scope creation, mediator/handler resolution, one send, and scope disposal.
Provider construction, registration, and first DI internal call-site generation are outside the measurement.
Same-scope rows resolve the mediator from the existing scope on every call, a different range from
the first typed-Send table.
DI-first lambda-only suites and competitors were not formally remeasured. DI-first behavior is
verified separately through package consumers.

### Variance and claim scope

BDN reported `ZeroMeasurement` for E02 Direct 0 behaviors and DirectVoid.
Since the delta over an empty method is unmeasurable, 0.069 ns / 0.004 ns are not used as
practical timings or ratio grounds.
Singleton resolve-included rows, concrete rows, and NativeVoid rows carried multimodality warnings.
Medians and deviations are reported together and all 24 values are kept. These are not compiler
warnings or run failures.

The post-fix exploratory comparison (20.6–81.0 ns) used a different runtime, JIT settings, invoker,
and suite.
It specified `DOTNET_TieredCompilation=0`; this run does not.
Smaller values on some routes do not mean RC2 optimized anything. The product implementation is unchanged.
RC1's 1–4 ns are not revived as current values; no competitor ranking, general allocation-free claim,
or per-app improvement rate is claimed.

### Improvements

On the consumer side, reusing an acquired mediator within the same live scope removes per-call
mediator resolution from the measured route.
Here, Scoped was 54.3 ns with DI acquisition versus 21.4 ns for an acquired send.
Keep required scope boundaries per request; do not merge differently-lived work into one scope
just for speed.
Singleton only where the mediator, handlers, Behaviors, and their dependencies are safe to share
across concurrent calls.

Product-side Send optimization starts with profiling generated continuations separately from
standard DI resolution, where dispatch dominates a real app.
The Scoped/Singleton-by-stage deltas point at investigation targets; they are not asserted to be
pure DI-resolution time.
Restoring a custom lifetime cache, pre-resolving nodes, or removing cancellation checks are
not improvement candidates.
Compare prototypes holding F01–F09, Transient, short-circuit, retry, and disposal on the same
suite and runtime.
Concrete calls showed no advantage here; speedups from call-site typing alone are not recommended.

## Stream formal measurements (34 routes)

2026-09-10. Chose Option A and remeasured 34 routes against the current implementation
including the post-RC1 DI lifetime and Stream disposal fixes.
To keep Preview numbers grounded, the old Hardening-1 values were not carried over;
the same existing suite was rerun.

Synchronously completing 0-behavior enumeration of 1024 items averaged 17.622 μs and 232 B/stream.
Direct was 12.947 μs and 120 B.
1024 items with async suspension took 648.388 μs and 113,959 B, about 111 B/item over Direct.
`OPT-stream-async` is updated to this current value and stays a separate-plan improvement candidate.

### Conditions and evidence

Used the product source at commit `a1033e9a521b2c22c50b17a0c120fded42093be7`.
The `HardeningStreams.cs` measurement code is unchanged since that commit
(for version and provenance, see [0.1.0 release notes](release/0.1.0-release-notes.md)).
Source/suite hashes, commit, and actual SDK info are retained as release evidence.

Conditions are BenchmarkDotNet 0.15.8, Release, Throughput, MemoryDiagnoser, 3 launches,
3 warmups / 8 measurements each, `OutlierMode=DontRemove`. Intel Core Ultra 7 258V,
Windows 11 10.0.26200.9278, SDK 10.0.300, runtime .NET 10.0.8,
X64 RyuJIT x86-64-v3, Concurrent Workstation GC. The PATH difference from the normal build
environment (SDK 10.0.400 / runtime 10.0.11) is recorded separately.

Ran after Send, in order, with no other build/test/AOT running.
All 34 routes succeeded: 19m20s total, exit code 0. No failures, reruns, or exclusions.
Kept 24 values per route, 816 total. Output matching across 34 routes was verified before measuring.

Rerun procedure, raw evidence, full console output, and aggregated numbers are retained as
release evidence, holding 816 values.
The JSON retains launch/pilot/overhead/warmup/measured operation counts and times.

### Results

Time is ns per operation, allocation is B per operation. Enumerating routes enumerate the stated
item count in one operation.
Medians and standard deviations are statistics over 24 iteration means, not p95/p99 per item.

| Route | Mean ns | Median ns | StdDev ns | B/op |
|---|---:|---:|---:|---:|
| Zr/0beh/0 | 89.831 | 88.623 | 8.120 | 232 |
| Direct/0beh/0 | 35.686 | 34.545 | 2.653 | 120 |
| Zr/0beh/1 | 97.550 | 96.945 | 1.571 | 232 |
| Direct/0beh/1 | 44.914 | 44.507 | 1.220 | 120 |
| Zr/0beh/16 | 363.175 | 362.061 | 6.369 | 232 |
| Direct/0beh/16 | 238.859 | 237.357 | 4.739 | 120 |
| Zr/0beh/1024 | 17622.119 | 17679.605 | 340.640 | 232 |
| Direct/0beh/1024 | 12947.087 | 12847.429 | 209.118 | 120 |
| Zr/0beh/16384 | 279225.114 | 279220.154 | 5699.720 | 232 |
| Direct/0beh/16384 | 214117.044 | 213032.727 | 6121.125 | 120 |
| Zr/1beh/16 | 758.703 | 750.888 | 48.804 | 424 |
| Zr/1beh/1024 | 35275.969 | 34792.067 | 2662.534 | 424 |
| Zr/3beh/16 | 1359.586 | 1311.403 | 109.502 | 808 |
| Zr/3beh/1024 | 58911.186 | 58817.192 | 935.304 | 808 |
| Zr/5beh/16 | 1968.049 | 1918.882 | 156.008 | 1192 |
| Zr/5beh/1024 | 89238.211 | 87500.250 | 4077.034 | 1192 |
| Zr/async/0beh/1024 | 648387.858 | 650486.377 | 23071.200 | 113959 |
| Direct/async/1024 | 343341.380 | 336349.109 | 45260.983 | 360 |
| Zr/async/5beh/1024 | 1448191.366 | 1445903.613 | 20266.531 | 118637 |
| Zr/generic/1024 | 18504.518 | 18491.325 | 467.852 | 232 |
| Direct/generic/1024 | 13740.382 | 13663.306 | 238.725 | 120 |
| Zr/create | 4.407 | 4.467 | 0.475 | 40 |
| Direct/create | 6.974 | 6.468 | 0.803 | 120 |
| Zr/first | 86.315 | 86.535 | 0.907 | 232 |
| Direct/first | 45.876 | 45.054 | 1.532 | 120 |
| Zr/break1 | 90.612 | 89.848 | 2.595 | 232 |
| Direct/break1 | 46.074 | 45.802 | 1.331 | 120 |
| Zr/break8 | 244.611 | 239.634 | 22.391 | 232 |
| Direct/break8 | 142.454 | 139.033 | 7.659 | 120 |
| Zr/tok-none | 386.146 | 379.729 | 20.737 | 232 |
| Zr/tok-api | 401.754 | 397.233 | 23.148 | 232 |
| Zr/tok-enum | 631.735 | 639.018 | 15.295 | 416 |
| Zr/tok-same | 651.288 | 653.290 | 15.750 | 416 |
| Zr/tok-different | 672.203 | 675.350 | 17.092 | 496 |

### Reading and limits

0/1/16/1024/16384 items with synchronously completing 0 behaviors were flat at 232 B for
Zendiator and 120 B for Direct.
Across the measured counts there is no per-item allocation growth; the route delta is 112 B/stream.
The 1024-item time delta totals 4.675 μs, about 4.57 ns/item, but items were not timed individually.

Synchronous enumeration with 1/3/5 behaviors added 192 B per stage over 0 behaviors.
Time does not scale at a fixed ratio with stage count. This Stream suite has no Direct chain
with Behaviors; when judging Behavior rows against Direct, note they include the Behaviors'
own work.

Creation-only Zendiator is 40 B versus 120 B for Direct. At that point Zendiator builds only the
outer lazy object and defers DI resolution and inner-iterator creation to the first MoveNext.
4.4 ns vs 7.0 ns for creation alone does not show whole-dispatch speed.
first/break rows include enumeration and disposal.

All tokens are uncancelled. enum/same/different rows include the existing forwarding async
iterator `ToBlocking`.
So none-vs-rest deltas are not attributed to token handling alone. same-vs-different, which share
the forwarder, differed by 80 B/stream here.
Cancellation correctness and detachment on disposal failure are verified separately by feature tests.

Zr 0-item, async Direct, and creation-only Zendiator rows carried multimodality warnings.
Values judged outliers were not excluded. Gen1/Gen2 collections were 0 on all 34 rows; Gen0 is
in the CSV/JSON.
`Zr16` and `ZrTokNone`, which do the same work, averaged about 6% apart; ordering and machine
variation remain.
Do not generalize small time deltas; read them with the table deviations and measured ranges.

### OPT-stream-async and improvements

Current 0-behavior async 1024 items: Zendiator 113,959 B, Direct 360 B.
The 113,599 B delta over 1024 items is 110.94 B/item, a separate current measurement from the
old ~109 B/item.
5-behavior async 1024 items was 118,637 B. Do not blend these into the 0 B/item of
synchronously completing routes.

The next improvement candidate is profiling allocations including the generated `MoveNextAsync`
async wrapper.
The current generator awaits the inner MoveNext as `async ValueTask<bool>`.
State retention on genuine suspension is a candidate source, but this measurement alone does not
locate every allocation site.
Profile allocations first, then try options such as reusable enumerator state under a separate plan.

Acceptance compares additional B/item over Direct and absolute time on the same runtime/suite,
holding lazy execution, cancellation,
mid-enumeration exit, exception propagation, double disposal, F09 linked-CTS detachment, and AOT.
RC2 implements no optimization. The candidate matters when async enumeration dominates a real app.
