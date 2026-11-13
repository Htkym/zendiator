# Zendiator

[日本語](README.ja.md)

A source-generated Mediator for .NET 10. Define handlers and ordered pipelines, then
call request-specific methods on the generated `IZendiator`. Typed dispatch uses
generated calls without runtime assembly scanning or reflective invocation.

## Install

Requires .NET 10 and C# 14. Install [Zendiator from NuGet](https://www.nuget.org/packages/Zendiator):

```shell
dotnet add package Zendiator --version 0.6.0
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

The capture changes in this section describe unreleased
[0.7.0 development](docs/release/0.7.0-release-notes.md). The installation example
uses published 0.6.0, which retains its existing capture behavior.

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
missing `[Handler]` for supported ZEN0035 diagnostics. Install version 0.6.0 with
`PrivateAssets="all"`. Fix All is limited to the current document; it does not
rewrite pipeline ordering, wrapper behavior or lifetimes. Command-line compilation
does not apply IDE fixes.

See the [0.6.0 release notes](docs/release/0.6.0-release-notes.md) and
[MediatR migration guide](docs/migrating-from-mediatr.md). Pre-1.0 APIs may change.

## Performance

Selected workloads measured on 2026-11-11. Each cell shows **mean time / allocated
bytes per operation (B/op)**; time and memory are separate metrics.

|Case|Zendiator|Immediate.Handlers 4.2.0|MediatR 14.2.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
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
|Async Hook success / first call|0.791 μs / 480 B|1.199 μs / 504 B|1.194 μs / 1,352 B|1.006 μs / 808 B|1.042 μs / 560 B|1.733 μs / 856 B|
|Async Hook success / repeated calls|0.030 μs / 0 B|0.049 μs / 0 B|0.557 μs / 944 B|0.515 μs / 496 B|0.067 μs / 0 B|0.488 μs / 480 B|
|Async Hook failure / first call, outer Hook|3.621 μs / 1,712 B|8.124 μs / 3,808 B|8.635 μs / 3,384 B|6.155 μs / 3,216 B|6.445 μs / 2,968 B|9.820 μs / 4,872 B|

New-scope cases include creation, first resolution, Send and disposal. Stream
values cover the complete operation; early break consumes one item from a
16-item input. Hook first-call, repeated-call and outer-failure cases have
different boundaries. N/A means no applicable comparison result.

Windows 11 x64, Intel Core Ultra 7 258V, SDK 10.0.403 / runtime 10.0.14,
BenchmarkDotNet 0.15.8, Release, affinity 1; 20 warmups, 12 measurements of
requested 500 ms, one launch per case. First/cold cases use 16384 invocations.
One launch does not establish a universal ranking or a controlled before/after
improvement. Foundatio uses its intercepted path; library API and DI shapes differ.

[Unrounded results](benchmarks/results/20261111-8848306-summary.csv) and
[full numeric observations](benchmarks/results/20261111-8848306-readme.json)
retain the measured source, additional workloads and uncertainty.
See [workload boundaries](benchmarks/README.md#read-the-comparison).

## Native AOT

Package-based consumers, including the standard typed Stream path, have been
published and run as win-x64 native executables. Runtime closure of open-generic
DI handlers with value-type arguments has known limitations; this does not mean
that struct cursors or struct upstream ports are unsupported. Other RIDs remain
unverified. Read the [known limitations](docs/release/known-limitations.md) before deployment.
