# Known limitations for 0.5.0

0.5.0 changes the handler, composition and Stream contracts.
Start with [getting started](../getting-started-0.5.0.md) and the
[Stream migration guide](../streaming-0.5.0.ja.md). Historical 0.4.0 APIs and results
are described in the [0.4.0 release notes](0.4.0-release-notes.md); they are not
compatibility promises for 0.5.0.

## Dependency capture and lifetime

Each Mediator lazily captures the first published successful resolution of each
dependency type, including Transient services and implementation factories. A new
send, retry or route does not imply a new instance. Native DI still constructs and
disposes these objects and controls Scoped or Singleton sharing.

Concurrent first resolutions can activate several Transients. Successful callers
use the publication winner; losing objects remain DI-owned. A resolution failure
still reports its own error even if another caller publishes an instance. Failures
and null results are not cached as successful activations.

Keep the scope alive until dispatch, enumeration or HTTP serialization and final
cleanup finish. Using a Mediator after scope disposal is unsupported. Scope disposal
does not cancel or join pending operations. Independent Stream owners do not make
shared handlers, hooks or sinks safe for concurrent use.

Lazy resolution means `ValidateOnBuild` alone cannot validate every dispatch
dependency. Exercise the routes your application uses, especially after changing
lifetimes. Do not let a Singleton Mediator capture Scoped services.

Closed storage is inline by default. Explicit lazy blocks can reduce retained empty
slots for some sparse compositions, but add block allocation and access costs for
others. Open generic cache promotion also has a cost. See the
[storage tradeoffs](../getting-started-0.5.0.md#closed-dependency-storage); neither
choice establishes a gain for all scopes.

## Completion and ambient context

Eligible plain `SendAsync` routes can fail synchronously before returning their
original `ValueTask`. Put both the call and its await inside a `try` block. Consume
the returned operation once; do not separately consume its `AsTask()` conversion.

Stream follows ordinary call and await context flow. Synchronous changes to
`AsyncLocal`, culture or `SynchronizationContext` can be visible to subsequent
callbacks or the caller; synchronous and pending completion need not expose the
same changes. Internal helpers are not ambient-state isolation boundaries. The
framework does not suppress `ExecutionContext`, replay an enumeration-wide saved
context, or guarantee a return to a UI context. Keep guaranteed operation data in
the request, cursor or session, and balance ambient scopes around their awaited use.

Segments with asynchronous Filter/Map hooks use one async move loop for source waits,
hooks and rejected-item retries. A synchronous hook's ambient change can reach the
next source pull after rejection, while synchronous source changes are restored
before outer cleanup or the caller resumes. Cleanup can thus observe different
ambient state than with the former helper boundaries. Keep cleanup resource identity
in explicit operation state.

## Stream ownership and use

`StreamAsync` returns standard `IAsyncEnumerable<T>`. Guarded enumerations have
independent owners and acquire dependencies and input on the first move. A flat
iterator route without Before, hooks, middleware or owned sessions and with two
noncancelable tokens instead acquires its input at `GetAsyncEnumerator`. The
producer's operation contract applies to this direct path, and the consumer must
await disposal even after EOF or failure. Forward the actual cancellation tokens;
do not remove cancellation to select the direct path. See the
[input and lifetime guide](../streaming-iterators.md#choose-an-input-contract).
An unconsumed guarded standalone source still allocates its first owner.

Use each enumerator sequentially. Await a pending move before another move or
disposal. On guarded paths, overlapping operations throw without canceling or
joining the first operation; pending disposal rejects new moves and shares
completion with repeated disposal. Cancellation cannot immediately stop I/O that
ignores its token.

On guarded paths, `Current` is valid after a successful move until the next accepted operation. EOF,
failure and disposal invalidate it. Custom cursors and `ValueTask` sources must obey
the .NET awaiter and single-consumption contracts; duplicate continuation delivery
and continuation registration that throws after accepting the callback are not
supported recovery paths.

On guarded paths, owned cursors and sessions are cleaned up once in reverse ownership order. Cleanup
failure takes precedence under ordinary `try/finally` rules. Borrowed DI services,
the provider and sink are not disposed by the generated owner. Middleware can reopen
its upstream only after normal EOF and completed cleanup; failure or early disposal
prevents restart. Explicit reopening can duplicate already delivered items, and no
automatic retry is supplied.

An asynchronous item-processing frame can retain rejected input references while
waiting for the next source item. Longer waits can mean longer retention. Release
after the pending operation settled and the cursor was disposed was observed in
representative checks; no general lifetime or retained-memory bound is established.
B/op results for value-type items do not include the memory cost of keeping a
reference graph alive.

`StreamSource(...).ConsumeAsync(...)` uses a fresh private owner. Its plan is not an
enumerable, and value-type sinks are passed by value: retain results in shared
reference state. This terminal does not promise lower time or allocation than
standard enumeration.

## HTTP and asynchronous work

Return `StreamAsync` naturally as `IAsyncEnumerable<T>` from an ASP.NET Core action.
Keep the request scope alive through serialization and cleanup, pass the request's
cancellation token, and do not return a source from an already disposed action-local
scope. The host serializer owns JSON framing and options. Once a response has
started, a later source or serializer failure may leave an incomplete response.

Functional HTTP integration does not establish HTTP throughput or external-I/O
performance. A `Task.Yield` workload is not a substitute for database, network or
storage behavior. Standard enumeration, typed consumption and HTTP are separate
performance evaluations.

## Performance evidence

No universal allocation-free or non-regression guarantee is made. Scope creation,
cold resolution, asynchronous suspension, middleware and cleanup have separate
costs. Improvements in one synchronous enumeration case do not establish a lead for
Streams that genuinely suspend; competitive superiority for those paths remains
unestablished. Early termination and some pending paths retain adverse observations.

The async Filter/Map comparison used a synchronous source with 1024 inputs and 1536
pending hook operations backed by synthetic ThreadPool scheduling. Full enumeration
showed lower time and allocation in that workload. Synchronous-completion and Early
intervals still included no improvement; Early accepted its first input. These results
do not establish gains for a pending source, five middleware stages, real I/O or HTTP.

The [README comparison](../../README.md#performance) reports selected workloads
from the measured 0.5.0 candidate, including unfavorable time and allocation
results. Historical 0.4.0 tables and other limited comparisons are separate evidence. Results from different source versions or
protocols must not be combined into a single-version ranking. The native Zendiator
cursor and competitors' compiler iterators are part of their complete product
paths, so a difference cannot be attributed to dispatch alone.

## CodeFix boundaries

The optional [CodeFix package](../../src/Zendiator.CodeFixes/README.md) adds a missing
handler marker and supports Fix All in the current document. It does not migrate
pipeline order, lifetimes, wrappers or other behavior automatically. The command-line
compiler does not apply code fixes; the IDE supplies Roslyn Workspaces assemblies.

## Native AOT and dispatch boundaries

The package verification target is win-x64; other RIDs are unverified. Tested open
generic DI handlers closed over value types require runtime generic construction
that is unavailable under Native AOT. Reference-type closures have package-consumer
coverage, which is not an every-type or every-RID guarantee.

Parallel Publish and runtime-object request/Stream dispatch are unsupported.
Notification type erasure is a separate supported path. User stages must follow
the sequential continuation and upstream-ownership contracts; retaining or invoking
continuations concurrently after their operation completes is not supported.
