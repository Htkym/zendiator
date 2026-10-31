# Zendiator

[日本語](README.ja.md)

A Mediator for .NET 10 with generated, request-specific dispatch and explicit,
ordered pipelines. Call through the generated `IZendiator`; the source generator
binds selected handlers, hooks and middleware without runtime assembly scanning or
reflective invocation on typed routes.

This branch describes the **unreleased 0.5.0 candidate**. It contains breaking API
changes. For the published 0.4.0 API, use the
[0.4.0 release notes](docs/release/0.4.0-release-notes.md) and its matching package.

- .NET 10, C# 14 and nullable reference types.
- Two core packages: `Zendiator.Abstractions` and `Zendiator`, versioned together.
- Optional IDE fixes: `Zendiator.CodeFixes`, installed separately at the same version.
- MIT licensed; pre-1.0 APIs may change.

## Get started

0.5.0 is not available as a published NuGet.org release. Obtain matching candidate
packages from your package feed. `Zendiator` includes the source generator and an
Abstractions dependency; a contracts-only project can reference
`Zendiator.Abstractions` alone. Keep package versions aligned across your application.

The [getting-started guide](docs/getting-started-0.5.0.md) explains candidate package
references and runnable samples. A minimal asynchronous request follows the same
pattern as the [GettingStarted sample](samples/Zendiator.Sample.GettingStarted/Program.cs):

```csharp
using GettingStarted.Generated.App;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace GettingStarted;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator<App>(configuration =>
        {
            configuration.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler));
            configuration.Pipeline<Greet>(DispatchFamily.SendAsync);
        });

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var greeting = await mediator.SendAsync(new Greet("Ada"));
        Console.WriteLine(greeting);
    }
}

[ZendiatorComposition("gettingStarted")]
public sealed class App;

public sealed record Greet(string Name) : IRequest<string>;

[ZendiatorHandler]
public sealed class GreetHandler : IRequestHandler<Greet, string>
{
    public ValueTask<string> HandleAsync(Greet request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new($"Hello, {request.Name}");
    }
}
```

Select marked handlers with `Handlers`. Declare hooks and wrappers in one ordered
`Pipeline` list. Handler-only `Send` and `SendAsync` routes can omit an empty
pipeline; a zero-stage Stream route declares one explicitly. Dedicated hooks can
be composed by generation. Use a wrapper for application-defined retry, caching
or continuation behavior; arbitrary wrapper semantics are not automatically
rewritten into hooks.

See the [ComposedApp sample](samples/Zendiator.Sample.ComposedApp/) for typed queries,
updates, synchronous Send, SendAll and Stream in one composition, and the
[migration guide](docs/getting-started-0.5.0.md#migrating-from-040) for open wrappers.

## Dispatch and dependency ownership

The generator emits concrete overloads for each selected request. Use its concrete
request type at the call site; runtime-object request dispatch is not supported.

| Operation | Entry |
|---|---|
| One synchronous result or synchronous command | `Send(request, token)` |
| One asynchronous result or asynchronous command | `SendAsync(request, token)` |
| Synchronous results from an explicit handler list | `SendAll(request, token)` |
| Asynchronous results from an explicit handler list | `SendAllAsync(request, token)` |
| Standard asynchronous enumeration | `StreamAsync(request, token)` |
| Sequential consumption by a sink | `StreamSource(request, token).ConsumeAsync(sink)` |

Notification dispatch remains sequential through `PublishAsync`; parallel publish,
fire-and-forget and persistence/outbox are not supplied. Notification registration
is separate from the composition's Send and Stream routes.

Generated composition registration uses one Scoped `IZendiator` entry and Transient
defaults for handlers, hooks and session factories. Register overrides before
`AddZendiator`; existing registrations are preserved. Each Mediator reuses captured
dependencies. Eligible asynchronous Hook routes acquire a route-local set before
the first Hook, including receivers left unused by an outer failure; other routes
retain per-dependency capture. Transient identity can differ between those sets
and other routes. See the [route capture contract](docs/getting-started-0.5.0.md#route-local-capture-for-asynchronous-hooks).
Reused handlers and hooks must support concurrent invocation.

DI owns activation and disposal of borrowed dependencies. Keep the caller's scope
alive through asynchronous completion, enumeration and cleanup. The Mediator does
not own the provider. Do not use it after that provider or scope has been disposed.
`ValidateOnBuild` alone cannot exercise dependencies reached only by dispatch.

Await each returned `ValueTask` once. Eligible zero-stage or synchronous-Before-only
`SendAsync` routes without factories, wrappers, sessions or completion callbacks
return the handler's original `ValueTask`. Dependency resolution and synchronous
callbacks run during the call, so an exception or
cancellation can throw before a result is returned. Put both the call and await
inside the application's `try` block. Other pipeline shapes have their own
completion boundaries; see the [execution contract](docs/design/0.5.0-route-and-stream-contract.md).

Closed dependency storage is inline by default. A composition can explicitly select
`ClosedServiceCacheMode.LazyBlocks` for sparse use in short-lived scopes. This changes
storage, not lazy activation or DI ownership, and can cost more for dense use or warm
reuse. The [storage guide](docs/getting-started-0.5.0.md#closed-dependency-storage)
explains the option, open-generic cache promotion and measured tradeoffs.

## Streaming and HTTP

A marked `IStreamHandler<TRequest,TItem>` returns `IAsyncEnumerable<TItem>` from
`Handle(request, token)`, so it can use a compiler async iterator directly. A marked
`IStreamSourceHandler<TRequest,TItem,TCursor>` opens a named class or struct
implementing `IAsyncEnumerator<TItem>` through `OpenAsync`. Dedicated Filter/Map
hooks preserve typed item flow; `Stage.Wrapper` connects an explicit
`IStreamMiddleware` boundary. The
[named-cursor sample](samples/Zendiator.Sample.GettingStarted/Program.cs) includes a
pending first move and an ordered Filter/Map pipeline.

Start with an iterator handler for ordinary asynchronous input. Use a named cursor
when its custom state or integration is needed; neither representation is always
faster. Select one source contract per route. The
[input and lifetime guide](docs/streaming-iterators.md#choose-an-input-contract)
compares acquisition, cancellation and disposal, with examples of both contracts.

`StreamAsync(request, token)` returns a generated source implementing standard
`IAsyncEnumerable<T>`. Consume it with `await foreach`, or return it naturally from
an ASP.NET Core action for the host's serializer. Keep the request's DI scope alive
until serialization and cleanup finish; do not create and dispose an action-local
scope before returning the source.

Guarded enumerations resolve and open their input on the first move, reject
overlapping operations and complete required cleanup before EOF or failure. A flat
iterator route with two noncancelable tokens instead acquires its input in
`GetAsyncEnumerator` and follows the producer's operation contract. For that direct
path, the consumer must await disposal even after EOF or a failed move. Forward the
real cancellation token, including `RequestAborted`; do not remove cancellation to
select a path. Borrowed services remain owned by DI.

For preprocessing that needs no request replacement or cursor lifetime, see
[Before versus middleware](docs/streaming-iterators.md#choose-before-or-middleware).

`StreamSource(...).ConsumeAsync(...)` is an optional terminal over the same pipeline.
It creates a fresh private owner and awaits sink callbacks sequentially. A `false`
result stops normally. Value-type sinks are passed by value, so put accumulated
results in a class sink or shared reference state. A typed terminal is not an
`IAsyncEnumerable` and does not guarantee lower time or allocation.

The framework does not guarantee UI context return or suppress `ExecutionContext`.
Do not rely on internal helpers to isolate ambient changes; use request, cursor or
session state for required data transfer. See
[Stream migration, HTTP and ownership](docs/streaming-0.5.0.ja.md) for cancellation,
early termination, middleware retries, context and disposal rules.

## Diagnostics and migration

Configuration diagnostics reject ambiguous handlers, unsupported contracts and
invalid composition settings. Suppressing a diagnostic does not enable a rejected
route. See [configuration diagnostics](docs/diagnostics/configuration.md).

The optional [CodeFix package](src/Zendiator.CodeFixes/README.md) can add a missing
`[ZendiatorHandler]` attribute for an unambiguous ZEN0035 diagnostic. Fix All is
limited to the current document. It does not migrate pipeline ordering, wrapper
logic, service lifetimes or ownership, and command-line compilation does not apply
IDE fixes.

The 0.4.0 Stream handler contract is replaced by an explicit enumerable or typed
cursor source contract. Migrate
handlers, explicit stage registration and context assumptions using the
[0.5.0 guide](docs/getting-started-0.5.0.md#migrating-from-040); source compatibility
with 0.4.0 is not promised.

## Performance

These observations are from the measured source snapshot below, completed on
2026-10-31; they are not a new measurement of the current branch HEAD. Selected
0.5.0 candidate workloads were compared on the same machine. Each cell shows
**mean time / allocated bytes per operation (B/op)**. Time and memory are separate
metrics, and this table does not cover every workload.

|Case|Zendiator candidate|Immediate.Handlers 4.2.0|MediatR 14.2.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
|---|---:|---:|---:|---:|---:|---:|
|Send / 0 behaviors|1.45 ns / 0 B|8.79 ns / 0 B|40.06 ns / 200 B|46.52 ns / 64 B|28.83 ns / 0 B|31.25 ns / 0 B|
|Send / 5 behaviors|10.16 ns / 0 B|16.81 ns / 0 B|135.93 ns / 1,016 B|83.05 ns / 544 B|33.28 ns / 0 B|148.19 ns / 0 B|
|Resolve again in the same scope + Send / 0 behaviors|27.30 ns / 0 B|40.73 ns / 0 B|71.09 ns / 200 B|83.82 ns / 64 B|56.91 ns / 0 B|68.67 ns / 0 B|
|Resolve again in the same scope + Send / 5 behaviors|30.13 ns / 0 B|45.40 ns / 0 B|151.70 ns / 1,016 B|113.12 ns / 544 B|57.99 ns / 0 B|180.64 ns / 0 B|
|New scope + 1 Send + dispose / 5 behaviors|151.47 ns / 704 B|117.48 ns / 568 B|202.87 ns / 1,400 B|178.18 ns / 920 B|633.17 ns / 1,712 B|412.67 ns / 856 B|
|Notification / 16 subscribers / sync|111.33 ns / 0 B|N/A|918.50 ns / 4,784 B|183.21 ns / 0 B|159.29 ns / 0 B|692.02 ns / 0 B|
|Stream 1024 items / 5 behaviors / sync|13.41 μs / 1,216 B|100.17 μs / 1,107 B|201.14 μs / 3,593 B|101.33 μs / 1,689 B|99.35 μs / 1,105 B|116.64 μs / 1,809 B|
|Stream 1024 items / 5 behaviors / async|165.82 μs / 116,562 B|263.09 μs / 1,754 B|490.69 μs / 4,726 B|261.25 μs / 2,339 B|245.04 μs / 1,755 B|289.13 μs / 2,539 B|
|Stream early break / 16-item input / 5 behaviors / sync|415.19 ns / 1,216 B|291.92 ns / 1,104 B|813.08 ns / 3,592 B|372.38 ns / 1,688 B|308.40 ns / 1,104 B|624.94 ns / 1,808 B|
|Stream early break / 16-item input / 5 behaviors / async|1.81 μs / 1,656 B|1.96 μs / 1,752 B|2.99 μs / 4,720 B|2.08 μs / 2,336 B|1.92 μs / 1,752 B|2.37 μs / 2,536 B|
|Behavior short-circuit / 3 stages / WarmDispatch|7.09 ns / 0 B|3.99 ns / 0 B|78.10 ns / 704 B|51.94 ns / 496 B|27.93 ns / 0 B|54.64 ns / 384 B|
|H success / FirstSend|739.22 ns / 480 B|1.24 μs / 504 B|1.21 μs / 1,352 B|1.04 μs / 808 B|1.01 μs / 560 B|1.71 μs / 856 B|
|H success / WarmSend|24.33 ns / 0 B|45.00 ns / 0 B|492.60 ns / 944 B|440.95 ns / 496 B|61.58 ns / 0 B|451.39 ns / 480 B|
|H failure / ColdOuterFailure|6.54 μs / 2,520 B|8.09 μs / 3,809 B|6.55 μs / 3,448 B|5.87 μs / 3,216 B|6.00 μs / 2,968 B|9.57 μs / 4,872 B|

The new-scope row includes creation, first resolution, Send and disposal. Stream
figures cover the whole operation, including creation, consumption and cleanup,
not one item. Early break consumes one item from a 16-item input. Sync/async
separates the enumeration completion patterns. H measures successful and
outer-failure asynchronous Hook paths; FirstSend, WarmSend and ColdOuterFailure
have different measurement boundaries, so their differences are not simply the
cost of throwing an exception. N/A means no applicable comparison result, not zero.

### Interpreting the results

- Some peers have lower time and allocation for new-scope and synchronous
  early-termination workloads.
- In the table's asynchronous Stream with 5 behaviors and 1024 items, Zendiator
  has a lower observed mean, but allocates 116,562 B versus 1,754–4,726 B for peers.
- Outside this table, **asynchronous Stream with 0 behaviors and 1024 items** is
  unfavorable in both time and memory: Zendiator is 149.19 μs / 115,458 B, versus
  53.90–92.56 μs / 393–865 B for peers.
- **Asynchronous Relay with 5 stages and 1024 items** is 150.97 μs / 116,562 B for
  Zendiator. Immediate, Mediator, DispatchR and Foundatio range from
  51.94–53.65 μs / 393–937 B; MediatR is 246.09 μs / 3,323 B.
- Synchronous Stream early break with 0 behaviors and a 16-item input is also
  58.49 ns / 160 B for Zendiator versus 42.80 ns / 144 B for Immediate.
  These observations do not establish general speed superiority or universal
  performance improvement.

### Measurement conditions

Measured source: `927827653e229b01afdd8bb97c00b9093ad049ab` (0.5.0 candidate).
Windows 11 x64, Intel Core Ultra 7 258V, SDK 10.0.403, .NET 10.0.14, DI 10.0.12,
BenchmarkDotNet 0.15.8, Release, affinity mask 1, 20 warmups, 12 measurements,
requested 500 ms iterations, and one launch per case.

Measurements span two execution periods, before an interruption and after
recovery. One launch does not establish independent-run reproducibility or a
controlled before/after improvement. Differences of a few nanoseconds are also
sensitive to benchmark overhead adjustment. Handlers are deliberately lightweight;
API and DI registration shapes differ between libraries. These results do not
predict application startup, HTTP or asynchronous I/O latency. Standard
enumeration and typed terminals are separate workloads.

Foundatio's rows here use its intercepted path, distinct from the native runtime
path used in a separate open-generic comparison. Historical 0.4.0 results are not
mixed into this table; this is not a release-wide ranking of all 0.5.0 compositions.

See the [benchmark measurement boundaries](benchmarks/README.md#read-the-comparison)
and [Stream comparison boundaries](benchmarks/README.md#stream-comparison-boundaries).
Their historical 0.4.0 table and data are not the source of these numbers. Running
the current fixture alone does not guarantee the pinned source, dependencies and
execution order used here.

## Native AOT and limitations

Generated typed dispatch does not use reflective invocation. Package-based
consumers cover smoke tests and Native AOT on win-x64. Open-generic DI closures over
value types have a known Native AOT limitation; other RIDs are unverified.
See [known limitations](docs/release/known-limitations.md) before deploying.

For migration from MediatR, use the
[migration guide](docs/migrating-from-mediatr.md), together with the current
[0.5.0 registration guide](docs/getting-started-0.5.0.md).

