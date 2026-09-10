# Zendiator

[日本語](README.ja.md)

A small Mediator for .NET 10 that generates typed dispatch at compile time.
A Roslyn Incremental Source Generator produces per-request `SendAsync` overloads,
struct continuation nodes, and DI registration. No runtime type switches,
reflection calls, or `dynamic`.

- Targets: .NET 10 (C# 14, nullable enabled)
- Distribution: 2 packages, `Zendiator.Abstractions` and `Zendiator` (versioned together)
- Current release: [0.1.1](docs/release/0.1.1-release-notes.md)
- Repository: https://github.com/Htkym/zendiator
- License: MIT

## Installation

```xml
<ItemGroup>
  <PackageReference Include="Zendiator.Abstractions" Version="0.1.1" />
  <PackageReference Include="Zendiator" Version="0.1.1" />
</ItemGroup>
```

Suggested project responsibilities:

| Project | References |
|---|---|
| Contracts (message definitions) | `Zendiator.Abstractions` only |
| Application (handlers, Behaviors, DI configuration) | `Zendiator.Abstractions` + `Zendiator` (for `AddZendiator`) |
| Host (startup, composition) | Application (calls `AddApplication()`-style wrappers) |

Place configuration where it won't create a back-reference from the handler side. Roslyn is not a runtime dependency. The generator itself ships inside the `Zendiator` package under `analyzers/dotnet/cs`.

## Usage

Define messages and handlers. You can use `class`, `record`, `struct`, and `record struct`.
Responses may use your own `Result` types or nullable types.

```csharp
using Zendiator;

public readonly record struct GetTargetYearQuery(int Year) : IQuery<MemorialTargetDto>;

public sealed class GetTargetYearQueryHandler : IQueryHandler<GetTargetYearQuery, MemorialTargetDto>
{
    public ValueTask<MemorialTargetDto> HandleAsync(GetTargetYearQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new(new MemorialTargetDto(query.Year, [$"Household-{query.Year}-1"]));
    }
}
```

Configure from your composition root — no attributes, no empty classes:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zendiator.DependencyInjection;

namespace MyApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "MyApp.Application.Generated";
            configuration.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>();
            configuration.RegisterServicesFromAssemblyContaining<ContractsAssemblyMarker>();
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
        });

        return services;
    }
}
```

```csharp
services.AddApplication();
```

- `IZendiator` is generated into the configured namespace.
- `SendAsync` generates overloads only for concrete request types.
  There is no generic send API taking `IRequest<T>` or `object`.
  Sending from a variable declared with a derived contract (such as `ICommand<T>`) is not supported. Only calls whose static type is the concrete type are covered.
- Attribute-based configuration (`[GenerateZendiator]` class or assembly
  attributes) remains available as a compatibility route; it cannot be combined
  with `AddZendiator` configuration lambdas in one compilation.

Calling code:

```csharp
services.AddApplication();
await using var scope = provider.CreateAsyncScope();
var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var result = await zendiator.SendAsync(new GetTargetYearQuery(2026));
```

Registration uses `TryAdd`. Pre-existing registrations are not replaced.
Handlers pre-registered as `Transient` stay `Transient`.
Repeated registration does not duplicate. `IZendiator` resolves the `Zendiator` with the same lifetime.

## Streaming

Define a stream request and handler. One request maps to exactly one handler.

```csharp
public sealed record GetHouseholdNames(int Count) : IStreamRequest<string>;

public sealed class GetHouseholdNamesHandler : IStreamRequestHandler<GetHouseholdNames, string>
{
    public async IAsyncEnumerable<string> HandleAsync(GetHouseholdNames request, CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return $"Household-{i}";
        }
    }
}
```

Register an open stream behavior alongside regular behaviors:

```csharp
configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
configuration.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>), order: 2);
```

Consume lazily. The handler starts on first `MoveNextAsync`, not on `StreamAsync`.
Either the API token or `WithCancellation` can cancel; different tokens are linked only when both are cancelable and different.

```csharp
await foreach (var name in zendiator.StreamAsync(new GetHouseholdNames(3), cancellationToken))
{
    Console.WriteLine(name);
}
```

`ref struct` requests use the sync contract (`ISyncRequest` + `SendSync`); async routes
and stream items diagnose them (ZEN0012). Re-enumeration is not guaranteed; call `StreamAsync` again for a fresh stream.
Open-generic handlers closed over a value type need runtime generic construction,
which NativeAOT cannot provide (see [known limitations](docs/release/known-limitations.md)).

## Lifetimes

The default is Scoped. Select Singleton or Transient per registration call:

```csharp
services.AddZendiator(static configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<GetTargetYearQueryHandler>();
    configuration.ServiceLifetime = ServiceLifetime.Singleton;
});
```

Rules:

- The default `ServiceLifetime` is Scoped. The value flows at runtime and never
  changes the generated structure, so providers may differ.
- The specified lifetime applies to newly added Zendiator, handler, and Behavior
  registrations. Existing registrations and their dependencies retain their own
  lifetimes; validate scopes to catch Singleton services capturing Scoped dependencies.
- First registration wins. A later registration does not replace existing registrations.
- Out-of-range values are diagnosed at generation (`ZEN0018`) when constant.
- Resolution follows DI semantics. With Transient, a new handler is created per send.
  The no-construction guarantee on short-circuit (downstream Behaviors and handlers are not resolved) is unchanged.

Use Singleton only when the mediator, handlers, Behaviors, and their dependencies are
safe to share across concurrent calls. Keep Scoped for services tied to a request scope.

## Behavior

Behaviors are DI-resolved `class`es; continuations are generated `readonly struct`s.
Continuations are never converted to delegates or interface variables.

```csharp
public interface IRequestContinuation<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> InvokeAsync(TRequest request, CancellationToken cancellationToken);
}

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync<TNext>(
        TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>;
}
```

Rules:

- Smaller `Order` wraps outer.
- Duplicating a Behavior type or an `Order` value is an error (ZEN0004).
- Closed Behaviors and two-argument open Behaviors of the form `Behavior<TRequest, TResponse>` are supported.
  Open Behaviors apply only to requests satisfying the constraints (`struct`, `class`, `notnull`, `new()`,
  base/interface, including nested generics).
- On short-circuit, downstream Behaviors and handlers are not resolved. A referenced-but-unconstructed handler is never created.
- Requests and `CancellationToken`s may be replaced when passed to next.
- Retries via sequential repeated `next` calls are supported. Parallel calls and retention after completion are out of scope.
- Null reference-type requests are rejected, cancellation is checked before each node runs. Exceptions are not translated.

See `samples/` for a runnable example. Its Contracts/Application/Host 3-project layout shows
a logging Behavior and success/failure handling with your own `Result<T, E>`.

```powershell
dotnet run --project samples/Zendiator.Sample.Host -c Release
```

## Multiple assemblies

Only the current compilation and assemblies named by configuration are inspected
via Roslyn symbols (`RegisterServicesFromAssemblyContaining<T>()` or
`RegisterServicesFromAssembly(typeof(X).Assembly)`).
Separately from whole-assembly scanning, the exact request types referenced by handler contracts are also picked up.
So even if you forget to include the assembly holding request types in the scan targets,
requests identical to or reachable from handlers become routes. To avoid unintended pickup,
review handler placement and assembly registrations.
Different spellings that resolve to the same assembly set share one generation unit.

## Diagnostics

| ID | Condition |
|---|---|
| ZEN0001 | No handler for the target request |
| ZEN0002 | Multiple handler implementations on a single request |
| ZEN0003 | Multiple response contracts, unsupported types (non-public, inaccessible, response mismatch) |
| ZEN0004 | Behavior contract mismatch, duplication, order conflict, closed Behavior matching no route |
| ZEN0005 | Bad generation-target declaration, duplication, reserved collisions |
| ZEN0006 | Conflicting generation modes (class + assembly attributes) |
| ZEN0007 | Invalid generation namespace or generated-name collision |
| ZEN0008 | Conflicting kinds (native-void/Unit, single/multi, sync/async, response/void multi) |
| ZEN0009 | Generic binding not inferable from the request |
| ZEN0010 | Ambiguous closed/open binding (single routes; multi routes fan out instead) |
| ZEN0011 | Incompatible constraints between request, handler, and Behavior |
| ZEN0012 | Invalid ref route (async/ref boxing, ref response, sync guidance) |
| ZEN0013 | Invalid subscriber or registration target |
| ZEN0014 | Reserved (never emitted; the notification erasure route is supported) |
| ZEN0015 | Attribute and DI configuration sources combined in one compilation |
| ZEN0016 | Multiple DI configurations with different structures |
| ZEN0017 | Unsupported configuration expression or callback shape |
| ZEN0018 | Invalid configuration value (namespace, duplicates, lifetime range, order conflicts) |
| ZEN0019 | Ambiguous registration binding (reserved) |
| ZEN0020 | `AddZendiator` call cannot be connected to generated registration |

## Public API

`Zendiator.Abstractions` (net10.0, no external dependencies) holds the contracts:

- Requests: `IRequest<TResponse>`, `ICommand<TResponse>`, `IQuery<TResponse>`,
  `IRequest`, `ICommand`, `IMultiRequest<TResponse>`, `IMultiRequest`,
  `ISyncRequest<TResponse>`, `ISyncRequest`, `ISyncCommand`,
  `ISyncMultiRequest<TResponse>`, `ISyncMultiRequest`, `INotification`, `Unit`,
  `IStreamRequest<TItem>`
- Handlers: `IRequestHandler<TRequest, TResponse>`, `IRequestHandler<TRequest>`,
  `ICommandHandler<TCommand, TResponse>`, `ICommandHandler<TCommand>`,
  `IQueryHandler<TQuery, TResponse>`, `INotificationHandler<TNotification>`,
  `ISyncRequestHandler<TRequest, TResponse>`, `ISyncRequestHandler<TRequest>`,
  `IStreamRequestHandler<TRequest, TItem>`
- Pipelines: `IRequestContinuation<TRequest, TResponse>`,
  `IRequestContinuation<TRequest>`, `IPipelineBehavior<TRequest, TResponse>`,
  `IPipelineBehavior<TRequest>`, `ISyncRequestContinuation<TRequest, TResponse>`,
  `ISyncRequestContinuation<TRequest>`, `ISyncPipelineBehavior<TRequest, TResponse>`,
  `ISyncPipelineBehavior<TRequest>`, `IStreamContinuation<TRequest, TItem>`,
  `IStreamPipelineBehavior<TRequest, TItem>`
- Attributes: `GenerateZendiatorAttribute`, `IncludeAssemblyAttribute`,
  `PipelineBehaviorAttribute`, `HandlerOrderAttribute`, `NotificationAttribute`

`Zendiator` (net10.0) holds the DI entry points:

- `ZendiatorConfiguration`: `Namespace`, `ServiceLifetime`,
  `RegisterServicesFromAssemblyContaining<T>()`,
  `RegisterServicesFromAssembly(Assembly)`, `AddOpenBehavior(Type, int)`,
  `AddOpenStreamBehavior(Type, int)`,
  `AddNotification<T>()`, `ConfigureHandlerOrder(Type, int)`, `Snapshot()`
- `ZendiatorConfigurationSnapshot`: frozen recorded values plus `GetFingerprint()`
- `ZendiatorServiceCollectionExtensions.AddZendiator` (parameterless and
  configuration-lambda overloads; uninterrupted calls fail fast)

Generated code per consumer compilation (`IZendiator`, `Zendiator`, and either
`ZendiatorServiceCollectionExtensions.AddZendiator` or the DI registrar plus
interceptors) is treated as part of the product.
After a stable release, package compatibility is verified against the previous stable release.
`0.1.1` is a `0.x` release: its API is not frozen permanently and may change before `1.0.0`.
The full list of contracts, handlers, pipelines, and attributes is in the Public API section above.

## Performance

`benchmarks/Zendiator.Benchmarks` compares direct calls against typed sends.
With warmed-up scopes and synchronously completing allocation-free handlers/Behaviors,
0 B of additional allocation per send is verified (the 0- and 1-stage sync paths are also pinned by tests).
First-time DI resolution, logging, and async suspension are outside that 0 B claim. No latency numbers are guaranteed.

Handlers and Behaviors are resolved through the bound DI provider for each invocation.
Scoped and Singleton reuse belongs to the container. The former mediator-side cache
was removed to preserve lifetime semantics when a service collection is used to build
multiple providers. The previous 1–4 ns figures no longer describe the current send path.

Current formal measurements (BenchmarkDotNet, Release, Throughput, three launches,
MemoryDiagnoser) are summarized in the [0.1.0 release notes](docs/release/0.1.0-release-notes.md),
with full tables in [formal measurements](docs/performance.md).
Values apply only to the measured routes and environment; generic response creation,
full scope lifecycle and asynchronously suspending streams have separate allocation costs.
No competitor ranking or general allocation-free claim is made.
See also [known limitations](docs/release/known-limitations.md), including `OPT-stream-async`.

## AOT and trimming

`IsAotCompatible` is set, and generated code uses no reflection.
AOT/trim warnings are treated as errors, not suppressed.
A full native AOT link needs the C++ workload. To publish an app as Native AOT
(adapt the project path to your own app):

```powershell
dotnet publish samples/Zendiator.Sample.Host -c Release -r win-x64 `
  -p:PublishTrimmed=true -p:TrimMode=full --self-contained
```

The verified matrix (AOT01-AOT12: typed, void, pipeline, generic, notification, multi,
sync/ref, stream, stream pipeline, generic stream, DI-first, assembly compat) and the one known
boundary (open generics closed over value types) are summarized in
[known limitations](docs/release/known-limitations.md).

## Out of scope (follow-ups)

Parallel publish, fire-and-forget, persistence/outbox, `Send(object)` for requests,
cycle detection, CodeFix, CodeLens, built-in `Result` pipeline mapping,
and built-in logging/validation are out of scope for the first release.
Sequential `PublishAsync`, streams via `StreamAsync`, and your own
`Result` types as ordinary `TResponse` values are supported.

For migrating from MediatR, see `docs/migrating-from-mediatr.md`.
