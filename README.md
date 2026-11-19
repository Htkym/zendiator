# Zendiator

[日本語](README.ja.md)

A source-generated Mediator for .NET 10. Define handlers and ordered pipelines, then
call request-specific methods on the generated `IZendiator`. Typed dispatch uses
generated calls without runtime assembly scanning or reflective invocation.

## Install

Requires .NET 10 and C# 14. Install [Zendiator from NuGet](https://www.nuget.org/packages/Zendiator).
The commands target 0.7.0 and require that version in the selected feed:

```shell
dotnet add package Zendiator --version 0.7.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
```

`Zendiator` includes the source generator and depends on `Zendiator.Abstractions`.
A contracts-only project can reference `Zendiator.Abstractions` alone. Keep all
Zendiator packages at the same version. The library is MIT licensed.

## Quick start

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

[Handler]
public sealed class GreetHandler : IRequestHandler<Greet, string>
{
    public ValueTask<string> HandleAsync(Greet request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new($"Hello, {request.Name}");
    }
}
```

`[Handler]` marks a handler. `Handlers<TRequest>` selects its implementation.
Declare hooks and middleware in an ordered `Pipeline<TRequest>` list. Handler-only
Send routes can omit an empty pipeline; Stream routes declare one explicitly.
Wrappers retain application-defined retry, caching and continuation behavior.
See the [configuration guide](docs/getting-started-0.5.0.md) and
[ComposedApp sample](samples/Zendiator.Sample.ComposedApp/) for complete examples.

## Dispatch

| Operation | Generated entry |
|---|---|
| Synchronous request | `Send(request, token)` |
| Asynchronous request | `SendAsync(request, token)` |
| Multiple synchronous results | `SendAll(request, token)` |
| Multiple asynchronous results | `SendAllAsync(request, token)` |
| Sequential notification dispatch | `PublishAsync(notification, token)` |
| Standard asynchronous enumeration | `StreamAsync(request, token)` |
| Sequential sink consumption | `StreamSource(request, token).ConsumeAsync(sink)` |

Use concrete request types at call sites. Runtime-object request dispatch,
parallel Publish, fire-and-forget and persistence/outbox are not supplied.
Notification registration is separate from Send and Stream routes.

## Dependencies and errors

The dependency capture rules below apply to [0.7.0](docs/release/0.7.0-release-notes.md).
Review the migration notes when upgrading from 0.6.0.

Generated registration uses a Scoped `IZendiator` and Transient defaults for
handlers, hooks and session factories. Register overrides before `AddZendiator`.
Each Mediator reuses captured dependencies. Eligible closed SendAsync hook routes,
including synchronous Before hooks, resolve all receivers in a route-local set
before the first hook. An outer failure may leave constructed receivers unused.
See [capture and lifetime](docs/getting-started-0.5.0.md#route-local-capture-for-asynchronous-hooks)
before relying on Transient identity or constructor timing.

DI owns activation and disposal. Keep the scope alive through dispatch,
enumeration and cleanup; do not use an entry after its scope is disposed.
Reused handlers and hooks must support concurrent invocation. `ValidateOnBuild`
can inspect registered route-set constructor graphs, but does not validate every
dependency reached during dispatch.

Await each returned `ValueTask` once. Put both a `SendAsync` call and its await
inside the application's `try` block: eligible direct routes can throw during the
call, before returning a `ValueTask`.

Dependency storage is inline by default. `ClosedServiceCacheMode.LazyBlocks`
changes storage costs for sparse compositions. `EagerDependencies` explicitly
changes activation timing and may construct unused services; it is not selected
automatically from inferred usage. See [storage options](docs/getting-started-0.5.0.md#closed-dependency-storage).

## Streaming

Choose one handler contract per route:

- `IStreamHandler<TRequest, TItem>` returns `IAsyncEnumerable<TItem>` from `Handle`.
- `IStreamSourceHandler<TRequest, TItem, TCursor>` opens a named class or struct
  cursor through `OpenAsync`.

Use `StreamAsync` with `await foreach`, or return it from an ASP.NET Core action
for normal asynchronous serialization. Keep the request scope alive until
serialization and disposal finish. `StreamSource(...).ConsumeAsync(...)` is an
optional sink terminal; it is not `IAsyncEnumerable<T>` and does not promise lower
time or allocation. Store accumulated results in a class sink or shared reference
state because value-type sinks are passed by value.

Standard middleware can keep the upstream port as a value type:

```csharp
public sealed class Relay<TRequest, TItem> : IStreamMiddleware<TRequest, TItem>
{
    public ValueTask<IAsyncEnumerator<TItem>> OpenAsync<TNext>(
        TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamUpstream<TRequest, TItem>
        => next.OpenAsync(request, cancellationToken);
}
```

The generator selects this concrete path for an accessible sealed class that
declares the public, nonvirtual generic implementation with the exact constraints.
Other supported receiver shapes retain interface dispatch. See the
[typed middleware migration guide](docs/migrating-stream-upstream.md).

Use enumerators sequentially, await pending moves before disposal, and dispose
after EOF, failure or early termination. Guarded routes acquire input on the first
move. Eligible flat iterator routes with noncancelable tokens acquire it at
`GetAsyncEnumerator`; forward the real cancellation tokens rather than changing
them to select a path. UI-context return and ambient-state isolation are not
guaranteed. See [input and ownership](docs/streaming-iterators.md) and
[execution rules](docs/release/known-limitations.md).

## Migration and IDE support

0.6.0 renames `ZendiatorHandlerAttribute` to `HandlerAttribute`. Change
`[ZendiatorHandler]` to `[Handler]` and rebuild consumers with matching packages.
Use `[global::Zendiator.HandlerAttribute]` if another name is ambiguous.
The standard Stream middleware interface now exposes `OpenAsync<TNext>`; see the
[migration guide](docs/migrating-stream-upstream.md) for adapters and concrete-call changes.

The optional [Zendiator.CodeFixes](src/Zendiator.CodeFixes/README.md) package adds a
missing `[Handler]` for supported ZEN0035 diagnostics. Install version 0.7.0 with
`PrivateAssets="all"`. Fix All is limited to the current document; it does not
rewrite pipeline ordering, wrapper behavior or lifetimes. Command-line compilation
does not apply IDE fixes.

See the [0.7.0 release notes](docs/release/0.7.0-release-notes.md),
[0.6.0 release notes](docs/release/0.6.0-release-notes.md) and
[MediatR migration guide](docs/migrating-from-mediatr.md). Pre-1.0 APIs may change.

## Performance

Development revision `0d53c53` had the lowest mean time on all 14 representative workloads in both execution orders. Stopping a synchronous Stream after one item took 317.31 ns and 296.50 ns, with 976 B/op in both runs. Asynchronous Stream early termination was the only workload whose confidence interval overlapped a competitor's. [Development results and measurement conditions (Japanese)](benchmarks/results/0d53c53-representative.ja.md)

Selected workloads measured from **Zendiator 0.7.0 source**. Each cell
shows **mean time / allocated bytes per operation (B/op)**; time and memory are
separate metrics.

|Case|Zendiator 0.7.0|Immediate.Handlers 4.3.0|MediatR 15.0.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
|---|---:|---:|---:|---:|---:|---:|
|Send / 0 behaviors|1.32 ns / 0 B|7.60 ns / 0 B|41.29 ns / 200 B|42.34 ns / 64 B|28.76 ns / 0 B|31.24 ns / 0 B|
|Send / 5 behaviors|5.35 ns / 0 B|15.69 ns / 0 B|137.64 ns / 1,016 B|90.68 ns / 544 B|38.26 ns / 0 B|159.67 ns / 0 B|
|Resolve again in the same scope + Send / 0 behaviors|26.74 ns / 0 B|40.16 ns / 0 B|59.78 ns / 200 B|77.37 ns / 64 B|55.17 ns / 0 B|65.44 ns / 0 B|
|Resolve again in the same scope + Send / 5 behaviors|43.15 ns / 0 B|47.85 ns / 0 B|169.09 ns / 1,016 B|127.66 ns / 544 B|68.31 ns / 0 B|200.01 ns / 0 B|
|New scope + 1 Send + dispose / 5 behaviors|104.35 ns / 624 B|120.43 ns / 568 B|217.41 ns / 1,400 B|172.70 ns / 920 B|662.06 ns / 1,712 B|310.43 ns / 856 B|
|Notification / 16 subscribers / sync|119.98 ns / 0 B|N/A|1014.79 ns / 4,784 B|194.24 ns / 0 B|164.72 ns / 0 B|732.58 ns / 0 B|
|Stream 1024 items / 5 behaviors / sync|12.42 μs / 1,016 B|93.65 μs / 1,104 B|187.00 μs / 3,594 B|94.25 μs / 1,689 B|95.00 μs / 1,105 B|110.33 μs / 1,809 B|
|Stream 1024 items / 5 behaviors / async|158.87 μs / 116,362 B|258.50 μs / 1,754 B|503.09 μs / 4,725 B|259.68 μs / 2,339 B|263.18 μs / 1,756 B|293.58 μs / 2,539 B|
|Stream early break / 16-item input / 5 behaviors / sync|393.74 ns / 1,016 B|286.42 ns / 1,104 B|806.12 ns / 3,592 B|380.91 ns / 1,688 B|312.13 ns / 1,104 B|625.86 ns / 1,808 B|
|Stream early break / 16-item input / 5 behaviors / async|1.98 μs / 1,456 B|2.05 μs / 1,752 B|3.17 μs / 4,720 B|2.28 μs / 2,336 B|2.16 μs / 1,752 B|2.76 μs / 2,536 B|
|Behavior short-circuit / 3 stages / repeated calls|4.67 ns / 0 B|4.63 ns / 0 B|83.07 ns / 704 B|57.47 ns / 496 B|28.47 ns / 0 B|58.94 ns / 384 B|
|Async Hook success / first call|0.908 μs / 448 B|1.302 μs / 504 B|1.231 μs / 1,352 B|1.145 μs / 808 B|1.065 μs / 560 B|1.790 μs / 856 B|
|Async Hook success / repeated calls|0.033 μs / 0 B|0.048 μs / 0 B|0.544 μs / 944 B|0.507 μs / 496 B|0.069 μs / 0 B|0.497 μs / 480 B|
|Async Hook failure / first call, outer Hook|3.757 μs / 1,680 B|8.467 μs / 3,808 B|7.011 μs / 3,448 B|6.740 μs / 3,216 B|6.239 μs / 2,968 B|10.231 μs / 4,872 B|

Send uses an already resolved entry unless stated otherwise. New-scope cases
include creation, first resolution, Send and disposal. Stream values cover the
complete operation; early break consumes one item from a 16-item input. Hook
first-call, repeated-call and outer-failure cases have different boundaries.
N/A means no applicable comparison result.

API and DI shapes differ; Foundatio uses its intercepted path. The measured
Stream/Relay inputs are a named cursor for Zendiator and compiler iterators for
peers. At Stream depth 5,
Zendiator forwards the cursor while peers enumerate/yield again at each stage.
The supplemental Relay cases match preprocessing-only stage work. Other libraries
can use less memory for new-scope Send and asynchronous full Stream, or less time
for synchronous early-stop Stream.

One machine and one launch per case do not establish a universal ranking or a
controlled release-to-release improvement. [All 122 results and uncertainty](benchmarks/results/0.7.0-2bc3726-summary.csv),
[full observations and provenance](benchmarks/results/0.7.0-2bc3726-readme.json),
and [conditions and reproduction](benchmarks/README.md#current-readme-comparison)
include the 39 supplementary cells and the exact measured source.

## Native AOT

Package-based consumers, including the standard typed Stream path, have been
published and run as win-x64 native executables. Runtime closure of open-generic
DI handlers with value-type arguments has known limitations; this does not mean
that struct cursors or struct upstream ports are unsupported. Other RIDs remain
unverified. Read the [known limitations](docs/release/known-limitations.md) before deployment.
