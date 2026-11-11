# Zendiator

[日本語](README.ja.md)

A Mediator for .NET 10 with generated, request-specific dispatch and explicit,
ordered pipelines. Call through the generated `IZendiator`; the source generator
binds selected handlers, hooks and middleware without runtime assembly scanning or
reflective invocation on typed routes.

This guide covers **0.5.1**. Read the [0.5.1 release notes](docs/release/0.5.1-release-notes.md)
for the fixes and experimental opt-in producer feature. Upgrading from 0.4.0 also
requires the breaking changes in the [0.5.0 release notes](docs/release/0.5.0-release-notes.md).
For the 0.4.0 API, use the
[0.4.0 release notes](docs/release/0.4.0-release-notes.md) and its matching package.

- .NET 10, C# 14 and nullable reference types.
- Two core packages: `Zendiator.Abstractions` and `Zendiator`, versioned together.
- Optional IDE fixes: `Zendiator.CodeFixes`, installed separately at the same version with `PrivateAssets="all"`.
- MIT licensed; pre-1.0 APIs may change.

## Get started

Install the two core packages at matching versions. `Zendiator` includes the source
generator and an Abstractions dependency; a contracts-only project can reference
`Zendiator.Abstractions` alone.

```shell
dotnet add package Zendiator --version 0.5.1
```

Before 0.5.1 is published to NuGet.org, use matching locally built packages from
your package feed. Keep package versions aligned across your application.

The [getting-started guide](docs/getting-started-0.5.0.md) explains package
references and runnable samples. When following its 0.5.0 package examples,
use 0.5.1 consistently for the current release. A minimal asynchronous request follows the same
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
It awaits sink callbacks sequentially. A `false`
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

The [current implementation](https://github.com/Htkym/zendiator/commit/88483069db6409e092efa0d5ef63452777f577c9) was compared on the same machine on 2026-11-11. All 101 non-N/A cells in the two tables were measured in this run. Each cell shows **mean time / managed allocated bytes per operation (B/op)**. Time and memory are separate metrics.

|Case|Zendiator (8848306)|Immediate.Handlers 4.2.0|MediatR 14.2.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
|---|---:|---:|---:|---:|---:|---:|
|Send / 0 behaviors|1.21 ns / 0 B|7.18 ns / 0 B|35.18 ns / 200 B|35.84 ns / 64 B|25.84 ns / 0 B|28.20 ns / 0 B|
|Send / 5 behaviors|9.38 ns / 0 B|16.78 ns / 0 B|130.72 ns / 1,016 B|79.88 ns / 544 B|34.16 ns / 0 B|155.00 ns / 0 B|
|Resolve again in the same scope + Send / 0 behaviors|24.51 ns / 0 B|35.45 ns / 0 B|54.81 ns / 200 B|77.00 ns / 64 B|53.51 ns / 0 B|63.15 ns / 0 B|
|Resolve again in the same scope + Send / 5 behaviors|40.05 ns / 0 B|44.60 ns / 0 B|148.61 ns / 1,016 B|115.75 ns / 544 B|57.53 ns / 0 B|185.89 ns / 0 B|
|New scope + 1 Send + dispose / 5 behaviors|148.91 ns / 704 B|113.39 ns / 568 B|208.90 ns / 1,400 B|151.09 ns / 920 B|406.93 ns / 1,712 B|413.65 ns / 856 B|
|Notification / 16 subscribers / sync|107.85 ns / 0 B|N/A|900.10 ns / 4,784 B|188.95 ns / 0 B|146.04 ns / 0 B|685.74 ns / 0 B|
|Stream 1024 items / 5 behaviors / sync|12.13 μs / 1,016 B|89.41 μs / 1,106 B|182.81 μs / 3,593 B|90.55 μs / 1,688 B|89.69 μs / 1,104 B|106.04 μs / 1,808 B|
|Stream 1024 items / 5 behaviors / async|147.51 μs / 116,362 B|229.28 μs / 1,753 B|448.74 μs / 4,724 B|227.71 μs / 2,338 B|226.34 μs / 1,755 B|280.33 μs / 2,538 B|
|Stream early break / 16-item input / 5 behaviors / sync|365.93 ns / 1,016 B|254.44 ns / 1,104 B|717.95 ns / 3,592 B|340.93 ns / 1,688 B|282.08 ns / 1,104 B|572.81 ns / 1,808 B|
|Stream early break / 16-item input / 5 behaviors / async|1.84 μs / 1,456 B|1.96 μs / 1,752 B|3.03 μs / 4,720 B|2.13 μs / 2,336 B|2.02 μs / 1,752 B|2.47 μs / 2,536 B|
|Behavior short-circuit / 3 stages / repeated calls|4.49 ns / 0 B|4.77 ns / 0 B|77.26 ns / 704 B|53.36 ns / 496 B|27.00 ns / 0 B|55.23 ns / 384 B|
|Async Hook success / first call|790.62 ns / 480 B|1.20 μs / 504 B|1.19 μs / 1,352 B|1.01 μs / 808 B|1.04 μs / 560 B|1.73 μs / 856 B|
|Async Hook success / repeated calls|30.31 ns / 0 B|48.63 ns / 0 B|556.91 ns / 944 B|514.80 ns / 496 B|67.41 ns / 0 B|487.82 ns / 480 B|
|Async Hook failure / first call, outer Hook|3.62 μs / 1,712 B|8.12 μs / 3,808 B|8.64 μs / 3,384 B|6.16 μs / 3,216 B|6.44 μs / 2,968 B|9.82 μs / 4,872 B|

New-scope timings include creation, first resolution, Send and disposal. Stream timings cover the entire operation, including cleanup; early break consumes one item from a 16-item input. Sync/async distinguishes enumeration completion patterns. First-call, repeated-call and outer-Hook-failure workloads have different boundaries, so their differences are not simply exception costs. N/A means no applicable comparison result, not zero.

### Supplementary workloads

|Case|Zendiator (8848306)|Immediate.Handlers 4.2.0|MediatR 14.2.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
|---|---:|---:|---:|---:|---:|---:|
|Stream 1024 items / 0 behaviors / async|140.56 μs / 115,457 B|49.48 μs / 393 B|77.98 μs / 865 B|51.86 μs / 457 B|49.60 μs / 393 B|53.53 μs / 392 B|
|Relay 1024 items / 5 stages / async|153.22 μs / 116,361 B|49.62 μs / 392 B|236.21 μs / 3,323 B|49.36 μs / 937 B|49.83 μs / 393 B|54.89 μs / 393 B|
|Stream early break / 16-item input / 0 behaviors / sync|58.43 ns / 160 B|39.96 ns / 144 B|134.95 ns / 536 B|73.01 ns / 208 B|66.07 ns / 144 B|66.29 ns / 144 B|

Both tables include workloads where peers can have lower time or allocation. These observations do not establish general speed superiority, universal non-regression or a controlled before/after improvement.

### Measurement conditions

Windows 11 x64, Intel Core Ultra 7 258V, SDK 10.0.403, .NET runtime 10.0.14 (dotnet host 11.0.0), DI 10.0.12, BenchmarkDotNet 0.15.8, Release, workstation GC, affinity mask 1; 20 warmups, 12 actual measurements, requested 500 ms iterations, one launch per case. First-call and cold-failure cases use 16384 invocations and unroll factor 1. BDN retained 9–12 statistical observations after its overhead adjustment and outlier handling.

The 64 UseCase cells and 37 restored Foundatio/ShortCircuit/H cells were collected in separate batches, with library rotation within each available comparison set. One launch and different collection times do not establish independent-run reproducibility. The Zendiator and Abstractions DLL bytes were identical across both batches.

[Unrounded results and uncertainty](benchmarks/results/20261111-8848306-summary.csv) include Mean, StdDev, standard error, confidence margin and retained N. [Full numeric observations](benchmarks/results/20261111-8848306-readme.json) include every BDN measurement, job settings, assembly hashes and the original report hashes; October results are not mixed into these tables.

Small differences of a few nanoseconds are sensitive to overhead adjustment. These lightweight workloads do not predict application startup, HTTP or asynchronous I/O latency. API and DI registration shapes differ between libraries; standard enumeration and typed terminals are different workloads. Foundatio uses its intercepted path here, not its native runtime open-generic path.

See [workload boundaries](benchmarks/README.md#read-the-comparison) and [Stream comparisons](benchmarks/README.md#stream-comparison-boundaries). The historical 0.4.0 data on that page is a separate measurement.

## Native AOT and limitations

Native AOT has been verified for package-based consumers on win-x64. Open-generic DI closures over value types
have a known Native AOT limitation; other RIDs are unverified.
See [known limitations](docs/release/known-limitations.md) before deploying.

For migration from MediatR, use the
[migration guide](docs/migrating-from-mediatr.md), together with the current
[0.5.0 registration guide](docs/getting-started-0.5.0.md).
