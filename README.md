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
`AddZendiator`; existing registrations are preserved. Each Mediator lazily captures
the first published successful instance of each dependency type, including
Transient dependencies. It reuses that instance across its routes. This does not
make a shared handler or hook thread-safe.

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

A marked `IStreamSourceHandler<TRequest,TItem,TCursor>` opens a named class or struct
implementing `IAsyncEnumerator<TItem>` through `OpenAsync`. Dedicated Filter/Map
hooks preserve typed item flow; `Stage.Wrapper` connects an explicit
`IStreamMiddleware` boundary. The
[named-cursor sample](samples/Zendiator.Sample.GettingStarted/Program.cs) includes a
pending first move and an ordered Filter/Map pipeline.

`StreamAsync(request, token)` returns a generated source implementing standard
`IAsyncEnumerable<T>`. Consume it with `await foreach`, or return it naturally from
an ASP.NET Core action for the host's serializer. Keep the request's DI scope alive
until serialization and cleanup finish; do not create and dispose an action-local
scope before returning the source.

Each enumeration has an independent owner. DI resolution, opening, sessions and I/O
start on its first move. Await a pending move before another move or disposal;
overlap is rejected. Borrowed services are not disposed by the pipeline. Acquired
cursors and owned sessions are cleaned up once, in reverse ownership order; cleanup
exceptions follow ordinary `try/finally` precedence.

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

The 0.4.0 Stream handler contract is replaced by named cursor opening. Migrate
handlers, explicit stage registration and context assumptions using the
[0.5.0 guide](docs/getting-started-0.5.0.md#migrating-from-040); source compatibility
with 0.4.0 is not promised.

## Performance

Completion pattern, pipeline depth, first use, DI lifetimes and consumer work all
affect performance. General speed improvement and non-regression across all
compositions are not established for the 0.5.0 candidate. Genuinely pending Full
Stream enumeration has not established a speed advantage over all compared
libraries. Early termination and new-scope workloads can remain unfavorable.
Standard enumeration, typed terminals and HTTP serialization are different
workloads; a result for one does not establish the performance of another.

The [historical benchmark snapshot](benchmarks/README.md#historical-040-results) and
[unrounded selected data](benchmarks/results/20261008-1c41d2b-summary.csv) describe
historical source `1c41d2b105073d2dc9be2c0e8684fdce2fa11f55` from 0.4.0. They are not a
full comparison of this 0.5.0 candidate. The final public 0.5.0 comparison is not yet
complete; results from different revisions are not presented as one release-wide
ranking. Lightweight benchmark handlers also do not predict application startup,
HTTP latency or asynchronous I/O latency.

## Native AOT and limitations

Generated typed dispatch does not use reflective invocation. Package-based
consumers cover smoke tests and Native AOT on win-x64. Open-generic DI closures over
value types have a known Native AOT limitation; other RIDs are unverified.
See [known limitations](docs/release/known-limitations.md) before deploying.

For migration from MediatR, use the
[migration guide](docs/migrating-from-mediatr.md), together with the current
[0.5.0 registration guide](docs/getting-started-0.5.0.md).