# Post-0.6.0 measurements

## Fixed conditions

The new baseline uses SDK 10.0.404, .NET runtime 10.0.14 x64, workstation GC,
Release, affinity mask 1, BenchmarkDotNet 0.15.8, 20 warmups, 12 measurements,
requested 500 ms iterations and one launch per case. `benchmarks/Directory.Build.props`
pins runtime 10.0.14 with roll-forward disabled for all four benchmark projects
when `ZendiatorFormalMeasurement=true`. The driver sets this property on its
restore/build commands and in the process environment inherited by BDN builds.
Ordinary non-measurement builds retain their normal runtime selection.
Formal builds also map BDN's physical artifact directory to
`/_/zendiator-bdn/` in compiler paths before the compile-input cache is computed.
This keeps generated source and PDB paths independent of the selected benchmark
project. The driver still requires one identical product DLL across cases.
Record the actual OS, CPU, loaded runtime and package-lock hashes with each run.

The package survey on 2026-11-13 selected these exact stable versions:

| Library | Pin | Primary source |
|---|---:|---|
| MediatR | 15.0.0 | [v15.0.0](https://github.com/LuckyPennySoftware/MediatR/releases/tag/v15.0.0), including its [net10.0 target](https://github.com/LuckyPennySoftware/MediatR/blob/v15.0.0/src/MediatR/MediatR.csproj) |
| Immediate.Handlers | 4.3.0 | [v4.3.0](https://github.com/ImmediatePlatform/Immediate.Handlers/releases/tag/v4.3.0) |
| Mediator.SourceGenerator / Abstractions | 3.0.2 | [stable v3.0.2](https://github.com/martinothamar/Mediator/releases/tag/v3.0.2); 3.1 prereleases are excluded |
| DispatchR.Mediator / Abstractions | 2.3.1 | [v2.3.1](https://github.com/hasanxdev/DispatchR/releases/tag/v2.3.1) |
| Foundatio.Mediator | 1.3.3 | [v1.3.3](https://github.com/FoundatioFx/Foundatio.Mediator/releases/tag/v1.3.3) |

MediatR 15.0.0 was released after the improvement plan's survey. The DI container
is fixed at 10.0.12 and DI abstractions at 10.0.15; the latter accommodates the
updated Immediate package. Logging abstractions are fixed at 10.0.9 in all four
measured projects. The driver checks shared output DLL hashes before timing and
retains the strict loaded-assembly checks during aggregation.
Restore and review every project's lock before the
run. Package pins and source changes do not establish new performance results.
The 2026-11-11 table used SDK 10.0.403 and older competitor versions, so its
numbers remain a separate measurement.

## Workloads and source identity

[`post-060-full-baseline.json`](post-060-full-baseline.json) contains 122 cells:
all 101 cells from the [2026-11-11 evidence](../results/20261111-8848306-readme.json),
18 additional Relay cells, and three Zendiator diagnostics. This covers all 83
main-table cells and the 18 historical outside-table cells. The added Relay rows
are full synchronous consumption and early break in both input modes, for all
six libraries. The diagnostics are Send FirstSend at depths 0 and 5 and ScopeK10
at depth 5.

The original ShortCircuit, Hook and Foundatio workload sources are tracked in
separate projects so their generated DI graphs do not enlarge the existing
UseCase composition. [`original-workloads.json`](original-workloads.json) records
the accepted archive and overlay-manifest hashes, the overlay's source head,
original/current hashes for 19 compiled workload files, and the three uncompiled
original `Program.cs.frozen` files. The two Zendiator handler markers change
from `[Z.ZendiatorHandler]` to `[Z.Handler]` for 0.6 compatibility. Repository
formatting is applied to the recovered sources; both transformations are
recorded per file. Current source hashes use Git's canonical LF bytes; original
source hashes retain the raw accepted bytes. Workload, registration and semantic
methods otherwise remain as recorded there. The overlay source head is
`927827653e229b01afdd8bb97c00b9093ad049ab`; it does not identify the whole later
README measurement tree at `88483069db6409e092efa0d5ef63452777f577c9`.

The supplemental projects use the new tracked shared entry
`zendiator-shared-measurement-v2`. Their project files, gate adapters, package
locks and shared measurement/artifact controls are outside the unchanged-source
claim. This is not a reconstruction of the old private measurement entry.
The exact historical preparation mode in
[`prepare_performance_fixtures.py`](../../scripts/prepare_performance_fixtures.py)
remains unchanged: it still requires the original three hash-pinned private
measurement tools and the original build hook. Those tools are not needed to
run this new baseline and have not been recovered.

| Matrix `Project` | Project directory | Preserved type identities and library aliases |
|---|---|---|
| omitted / `usecase` | `Zendiator.UseCaseBenchmarks` | `Zendiator`, `Immediate`, `MediatRHistorical`, `Mediator`, `DispatchR` prefixes |
| `foundatio` | `FoundatioReleaseComparison` | `FoundatioSend*`, `FoundatioStream*`, `FoundatioRelay*`, `FoundatioNotification16` map to Foundatio |
| `short` | `ShortCircuitComparison` | `LShortCircuit`; `MediatRShortCircuit` maps to the MediatR library |
| `hooks` | `HPeerComparison` | `HLBenchmarks`; `HMediatRHistoricalBenchmarks` maps to the MediatR library |

The legacy class prefix `MediatRHistorical` is a saved case identity, not a claim
that new runs use an old package. The [82-cell UseCase-only selection](post-060-baseline.json)
remains available as a smaller diagnostic matrix. It omits the supplemental
projects and is not the complete baseline or common non-regression gate.

## Public row mapping and comparison boundaries

Here `L` is the library prefix selected by the project table above. The full
matrix supplies the exact type, method and parameters for each cell. Existing
row identifiers below refer directly to the `case` values in the historical
numeric evidence; `outside-*` rows are supplemental, not main-table rows.

| Row identifier | Type / method | Parameters and timed boundary |
|---|---|---|
| `send0`, `send5` | `LSend0.Typed`, `LSend5.Typed` | Scoped entry already resolved; one lightweight Send |
| `resolve-send0`, `resolve-send5` | `LSend0.ResolveSend`, `LSend5.ResolveSend` | Scoped entry resolved again in the same scope, then Send |
| `scope-send5` | `LSend5.ScopeK1` | Scope creation, first entry resolution, one Send and disposal |
| `notification16` | `LNotification16.Dispatch` | Sixteen sequential subscribers; Immediate has no cell |
| `stream5-full-sync`, `stream5-full-async` | `LStream5.Full` | Count 1024, Asynchronous false/true; full consumption and disposal |
| `stream5-early-sync`, `stream5-early-async` | `LStream5.EarlyBreak` | Count 16, Asynchronous false/true; consume one item and dispose |
| `short3-warm` | `LShortCircuit.WarmDispatch` | Three declared wrappers; the first returns 42; inner stages and Handler do not execute; warmed entry and a live cancellable token |
| `hook-first`, `hook-warm`, `hook-outer-failure` | `HLBenchmarks.FirstSend`, `WarmSend`, `ColdOuterFailure` | Three full async Hook stages whose APIs complete synchronously; first/outer-failure include first entry resolution from pre-created scopes; warm repeats an already warmed entry |
| `outside-stream0-full-async` | `LStream0.Full` | Count 1024, Asynchronous true; Handler input only |
| `outside-stream0-early-sync` | `LStream0.EarlyBreak` | Count 16, Asynchronous false; one item and disposal |
| `outside-relay5-full-async` | `LRelay5.Full` | Count 1024, Asynchronous true; preprocessing-only stages |
| Added Relay rows | `LRelay5.Full`, `LRelay5.EarlyBreak` | Full: 1024/sync; EarlyBreak: 16/sync and 16/async; preprocessing-only stages |
| Added diagnostics | `ZendiatorSend0.FirstSend`, `ZendiatorSend5.FirstSend`, `ZendiatorSend5.ScopeK10` | FirstSend excludes scope creation/disposal; ScopeK10 includes a new scope, resolution, ten Sends and disposal |

Stream/5 currently has different stage processing: Zendiator forwards its
upstream cursor, while the other libraries enumerate and yield each item again
at each stage. Relay is the processing-matched comparison: every library only
preprocesses and directly returns upstream. Keep both results with this distinction.

The baseline Zendiator Stream/Relay input is the named `WorkStreamCursor`; peers
use the shared compiler iterator `Work.Stream`. The named cursor's per-item
asynchronous helper allocation is part of this baseline. The existing
[`same-producer-streams.json`](../Zendiator.UseCaseBenchmarks/same-producer-streams.json)
isolates the common iterator at depth 0. PERF-04 changes the representative
input and must show the old and new conditions together before replacing a row.

Selected Send entries are Scoped, but dependency lifetimes are not identical.
UseCase retains each library's fixture registration. Hook/Short normalize their
logical receivers to Transient while preserving native capture and disposal.
Foundatio's main fixture retains Scoped handlers/middleware and its native
interceptors. Keep registration descriptor evidence with the results; do not
present the matched entry lifetime as matching every dependency's lifetime.

## Run, retain and decide

Commit the source and restored locks before formal timing. Use a short source
path on Windows, an unused output directory outside that worktree, and a machine
window without other builds, tests or benchmarks. From the repository root:

```powershell
python benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py --group custom --matrix benchmarks/performance-plan/post-060-full-baseline.json --output D:/z3/o/full-baseline --expect-sdk 10.0.404
```

For a planning-only check, add `--dry-run`. Resume with `--resume` only when the
source, SDK, matrix and output remain identical and the driver has verified
termination of the previous owned process trees. Keep the complete output:
`session.json`, source/package/environment manifests, execution plan, per-suite
semantic proofs, per-case logs and full BDN JSON, loaded-assembly maps, preserved
consumer bundles, artifact binding/proof files, run-info and `all-results.csv`.
The selected project and actual report type/method/parameters identify each row.
The original 389-case UseCase gate and separate struct-upstream gate remain;
Short, Hook and Foundatio run their own original semantic checks through the
shared entry and again in the preserved measured consumer.

[`perf-01.json`](perf-01.json) is the 15-cell adoption/non-regression selection:
Zendiator and Immediate ScopeK1/ScopeK10, Zendiator Send0/5 Typed and ResolveSend,
Notification16, synchronous Stream5 Full, ShortCircuit, all three Hook cases and
Send5 FirstSend. Use this same selection for the controlled PERF-01 comparison;
the full baseline alone does not establish the change's improvement.

[`perf-02a.json`](perf-02a.json) selects 31 cases for the initial Stream scope-probe
change: the ten common non-regression cases, standard and typed Stream Full/EarlyBreak
at depths zero and five in both input modes, two Immediate synchronous early-break
controls, and Zendiator/Immediate Relay early termination in both input modes.
The synchronous Stream5 Full case appears only once. Each A/B block has 31 pairs
and 62 timed steps. Keep the existing named-Cursor input and workload definitions;
Relay matches preprocessing work but does not yet match the input representation.

For step a, assess startup improvement and the common, asynchronous and typed
non-regression boundaries. Record the same-block gap to Immediate; the final
PERF-02 early-break target is assessed after steps b/c. Semantic checks retain
root startup, deferred inner opening/reopening, StreamBefore revalidation and
DI ownership. One initial root probe does not mean one total probe for routes
with StreamBefore callbacks. No PERF-02a measurements or adoption are recorded here.

Commit this matrix and measurement documentation before the step-a code change.
Use that infrastructure-only commit as the baseline and the later product commit
as the candidate, with identical controls in the controller and both worktrees.
This two-commit sequence can stay in one PERF-02a PR without changing its parent
PR. Choose and freeze the product parent after PERF-01's adoption decision; this
matrix does not adopt PERF-01.

### Controlled revision comparison

`run_ab.py` prepares both clean, immutable short worktrees before timing, then
calls each worktree's own `run_interleaved.py` worker for one case at a time.
Benchmark sources, matrices, package locks, project/build files and SDK inputs
must have identical Git blobs; LF/CRLF checkout differences are allowed, while
each side retains its immutable working-byte digest. Product source revisions
may differ. Commit the common
measurement infrastructure before branching the performance candidate. Keep
both worktrees fixed until the block and its retained-artifact checks finish.
The A/B controller currently requires Windows nested Job containment; the
single-revision driver retains its existing platform support.

```powershell
$baselineCommit = git -C D:/z3/r0 rev-parse HEAD
$candidateCommit = git -C D:/z3/r1 rev-parse HEAD
python benchmarks/Zendiator.UseCaseBenchmarks/run_ab.py --baseline D:/z3/r0 --candidate D:/z3/r1 --baseline-sha $baselineCommit --candidate-sha $candidateCommit --matrix benchmarks/performance-plan/perf-01.json --output D:/z3/o/perf-01-forward --expect-sdk 10.0.404
```

Use `worker-smoke.json` with two worktrees at the same revision to validate the
worker phases and actual A/B artifact bindings before comparing a candidate.
This single-cell matrix is a harness check; use the task matrix for adoption.
Cases stay grouped by the existing comparison key. Baseline/candidate results
are adjacent, and the first side alternates by key and member. The retained
parent plan and UTC/monotonic step records prove the actual serial order. Each
side keeps its own session, strict per-case DLL identities, complete measured
consumer bundles and saved-consumer gates. `comparison.json` preserves BDN's
confidence level, effective N, bounds and allocation values. Disjoint intervals
are classified as improvement or regression; overlapping intervals are labelled
`overlap`. Ratios are point estimates. No task-specific adoption is automated.

Declare reverse-run conditions before timing: use a reverse block if an unchanged
peer control has disjoint A/B intervals, an adoption boundary remains unresolved,
or the apparent outcome depends on which side runs first. Add `--reverse` with
a new `--output`; it completely reverses the forward schedule, including side
order. Keep both blocks separate, report disagreement as unresolved, and do not
pool them or select whichever block is favorable. Two independent whole-revision
runs do not establish a controlled gain.

Resume with the same command plus `--resume` only when completed step records form
an unchanged prefix and all owned processes are verified terminated. A running
or uncertain step, an unrecorded result, or incomplete preparation blocks resume;
retain the evidence and use a new output after resolving the cause.

At the requested 500 ms, 20 warmups plus 12 measurements amount to about 33
minutes of nominal iteration time for 122 cells and four minutes for 15 cells.
Fixed-invocation FirstSend/ColdOuterFailure cases can finish iterations sooner;
pilot, overhead, compilation and preserved-consumer verification add time.
Reserve roughly 1.5–3 hours for the full run and 20–45 minutes for the focused
selection. These are planning estimates, not observed runtimes.

CI must compile the three supplemental projects and execute their semantic
entries without timing. Those entries require runtime 10.0.14, so CI needs that
runtime in addition to its selected SDK. Installing SDK 10.0.403 alongside the
current 10.0.x SDK supplies that runtime; the repository still selects SDK
10.0.404. Build the supplemental projects with
`-p:ZendiatorFormalMeasurement=true`, then set `COLD_RUN` to an unused runner-temp
directory for each `dotnet <project>/bin/Release/net10.0/<project>.dll --validate-only`.


Judge time and allocation separately. A fastest claim requires the lowest mean
and a confidence interval that does not overlap the runner-up; overlap is
considered equivalent for this plan. Minimal allocation requires no more B/op
than every measured peer in that same run. PERF-01 requires ScopeK1 to beat
Immediate without interval overlap, no increase over 704 B, and the complete
common non-regression set. Preserve exact measured conditions and the adoption
result rather than inferring a universal ranking or a release-to-release gain.
