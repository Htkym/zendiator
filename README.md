# Zendiator

[日本語](README.ja.md)

A small Mediator for .NET 10 that generates typed dispatch at compile time.
A Roslyn Incremental Source Generator produces per-request `SendAsync` overloads,
struct continuation nodes, and DI registration. Typed request dispatch needs no
runtime assembly scanning, reflective invocation, or `dynamic`.

- Targets: .NET 10 (C# 14, nullable enabled)
- Distribution: 2 packages, `Zendiator.Abstractions` and `Zendiator` (versioned together)
- Status: pre-1.0 preview; breaking changes are allowed
- Repository: https://github.com/Htkym/zendiator
- License: MIT

## Installation

```shell
dotnet add package Zendiator --version 0.4.0
```

`Zendiator` includes an Abstractions dependency and the source generator. A
contracts-only project can reference `Zendiator.Abstractions` instead. Pin the
package versions used by your application and keep both packages aligned.

This README describes 0.4.0. See the [release notes](docs/release/0.4.0-release-notes.md)
for API and migration changes when upgrading.

Suggested project responsibilities:

| Project | References |
|---|---|
| Contracts (message definitions) | `Zendiator.Abstractions` only |
| Application (handlers, Behaviors, DI configuration) | `Zendiator` (includes Abstractions transitively) |
| Host (startup, composition) | Application (calls `AddApplication()`-style wrappers) |

Place configuration where it won't create a back-reference from the handler side. Roslyn is not a runtime dependency. The generator itself ships inside the `Zendiator` package under `analyzers/dotnet/cs`.

## Usage

Register the current compilation with one call. No separate initialization or
custom provider is required:

```csharp
using Zendiator.DependencyInjection;

services.AddZendiator();
```

Use the normal host `builder.Build()` or standard DI container construction. Use
the configuration lambda shown below when you need other assemblies, behaviors,
or an explicit generated namespace.

Define messages and handlers. You can use `class`, `record`, `struct`, and `record struct`.
Responses may use your own `Result` types or nullable types.

```csharp
using Zendiator;

public sealed record MemorialTargetDto(int Year, IReadOnlyList<string> Names);

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

For a multi-project application, configure from the composition root. Attributes
and an empty mediator class are not required:

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
- `SendAsync` generates request-specific overloads, including supported generic request shapes.
  There is no generic send API taking `IRequest<T>` or `object`.
  Sending from a variable declared with a derived contract (such as `ICommand<T>`) is not supported. Only calls whose static type is the concrete type are covered.
- Attribute-based configuration (`[GenerateZendiator]` class or assembly
  attributes) remains available as an alternative declaration style; it cannot be combined
  with `AddZendiator` configuration lambdas in one compilation.

Calling code:

```csharp
using MyApp.Application.Generated;

var services = new ServiceCollection();
services.AddApplication();
await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var result = await zendiator.SendAsync(new GetTargetYearQuery(2026));
```

Registration uses `TryAdd`. Pre-existing registrations are not replaced.
Pre-registered lifetimes are preserved, but each mediator lazily captures its first Handler and Behavior instance, including `Transient` dependencies.
Repeated registration does not duplicate. The mediator has one type registration,
`IZendiator` to `Zendiator`; the concrete `Zendiator` type is not registered separately.
Resolve or inject `IZendiator`. To customize construction, register the factory for
`IZendiator` instead of `Zendiator`:

```csharp
services.AddScoped<IZendiator>(provider => new Zendiator(provider));
services.AddZendiator();
```

Direct `new Zendiator(provider)` remains supported. A separate concrete-type
registration does not change how `IZendiator` is created.

## Streaming

Define a stream request and handler. One request maps to exactly one handler.

```csharp
public sealed record GetHouseholdNames(int Count) : IStreamRequest<string>;

public sealed class GetHouseholdNamesHandler : IStreamRequestHandler<GetHouseholdNames, string>
{
    public async IAsyncEnumerable<string> HandleAsync(
        GetHouseholdNames request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
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

For lightweight synchronous input checks, implement a validator and register its closed concrete type explicitly:

```csharp
public sealed class HouseholdNameValidator : IStreamRequestValidator<GetHouseholdNames>
{
    public void Validate(GetHouseholdNames request) => ArgumentOutOfRangeException.ThrowIfNegative(request.Count);
}

configuration.AddStreamRequestValidator(typeof(HouseholdNameValidator), order: 0);
```

Validation is opt-in; assembly scanning does not register validators automatically.
Each registered type must be an accessible, non-abstract closed class. All of its
`IStreamRequestValidator<TRequest>` contracts must exactly match closed generated
stream routes. Open-generic validators and base-request matching are unsupported.
Types and validator orders must be unique within the composition; validator order
is independent of behavior order.

Validation runs synchronously once per `StreamAsync` call, in ascending order,
before an enumerable is created. It does not run again when that sequence is
enumerated. Registered reference requests reject null at entry; streams without
validators retain their null check on first `MoveNextAsync`. Cancellation is
checked during enumeration, so entry validation also runs with an already-canceled
API token. A failure stops later validator and pipeline resolution.

For attribute configuration, use
`[StreamRequestValidator(typeof(HouseholdNameValidator), Order = 0)]` on the
generated mediator or assembly. Do not mix attributes with a configuration lambda.
Validators must be synchronous, input-only and safe for concurrent calls; keep
asynchronous work and I/O in the handler or stream behavior. Keep the validated
input stable and the DI scope valid until enumeration completes. Captured validator
instances are reused within a mediator, including Transient registrations.
A validator that is also a handler or behavior may be constructed at entry;
its pipeline method still runs lazily. See [known limitations](docs/release/known-limitations.md).

Consume lazily. The handler starts on first `MoveNextAsync`, not on `StreamAsync`.
Either the API token or `WithCancellation` can cancel; different tokens are linked only when both are cancelable and different.

If startup fails, that enumerator does not restart: subsequent `MoveNextAsync()` calls return `false`.
Dispose it even after failure, for example with `await using`. Call `StreamAsync` again to retry with a fresh enumeration.

```csharp
await foreach (var name in zendiator.StreamAsync(new GetHouseholdNames(3), cancellationToken))
{
    Console.WriteLine(name);
}
```

Consume each `MoveNextAsync()` or `DisposeAsync()` result once, following the `ValueTask` contract.
If the same operation must be awaited more than once, call `AsTask()` once before
consuming it and reuse the returned `Task`, without also consuming the original
`ValueTask`. Await the current move before starting the next; `await foreach`
already does this. [CA2012](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2012)
can help detect incorrect consumption.

`await using` consumes the disposal result once. Pooled disposal may retain a
reference to the enumerator until its returned `ValueTask` is consumed, even
after disposal completes.

`ref struct` requests use the sync contract (`ISyncRequest` + `SendSync`); async routes
and stream items diagnose them (ZEN0012). Re-enumeration is not guaranteed; call `StreamAsync` again for a fresh stream.
Open-generic handlers closed over a value type need runtime generic construction,
which NativeAOT cannot provide (see [known limitations](docs/release/known-limitations.md)).

## Synchronous multiple results

For `ISyncMultiRequest<T>`, `SendAllSync(request, cancellationToken)` returns handler results
in order as `IReadOnlyList<T>`. Do not depend on a concrete collection type.

To reuse caller-owned storage, use the Span overload. This example assumes that
`GetValues` has two handlers returning `int`:

```csharp
Span<int> results = stackalloc int[2];
int written = zendiator.SendAllSync(new GetValues(), results, cancellationToken);
// results[..written] contains the results in handler order.
```

All three arguments are required. Validation checks a null request, pre-cancellation,
then capacity. Insufficient capacity throws `ArgumentException` before resolving handlers
or dependencies. The dispatcher writes results only after all handlers succeed and leaves
unused capacity untouched. On failure, it does not write to the destination; handler side
effects are not rolled back. Input `ReadOnlySpan<T>` and output storage may overlap because
output is written after all handlers execute.

For reference-type results, pass a Span over an array owned by the caller. Reusing storage
avoids the result-container allocation; first-time DI resolution and handler allocations
remain separate. `SendAllAsync` does not accept a Span destination.

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
- `ServiceLifetime` controls the mediator. Generated handlers and Behaviors default
  to Transient when the mediator is Scoped; they are still resolved once per
  mediator. Singleton and Transient mediators use their selected lifetime for
  generated dependencies. Existing registrations retain their own lifetimes.
- To retain scope-wide sharing with other DI consumers, set
  `configuration.DependencyLifetime = ServiceLifetime.Scoped` or register the
  affected dependency as Scoped before `AddZendiator()`. Validate scopes to catch
  Singleton services capturing Scoped dependencies.
- First registration wins. A later registration does not replace existing registrations.
- Out-of-range values are diagnosed at generation (`ZEN0018`) when constant.
- Each mediator captures dependencies on first use and reuses them, including Transient dependencies. Use a new scope for a new default Scoped mediator, or configure a Transient mediator for a fresh composition on each resolution.
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
| ZEN0021 | Explicitly treating a generated mediator as `IDisposable` |
| ZEN0022 | Returning a mediator resolved from a `using` scope |
| ZEN0023 | Sending after explicitly disposing its scope in the same method |
| ZEN0024 | Invalid closed Stream validator type or unmatched validator contract |

The lifetime analyzer's ZEN0021–ZEN0023 are warnings for directly provable cases, not a complete proof of scope safety. See the [migration guide](docs/migrating-from-mediatr.md) for fixes.

## Public API

The source generator creates a concrete overload for each configured route. Call through the generated `IZendiator` with a concrete request type.

|Contract|Generated operation|
|---|---|
|`IRequest<T>`, `ICommand<T>`, `IQuery<T>`|`SendAsync(request, cancellationToken)`|
|`IRequest`, `ICommand`|`SendAsync(request, cancellationToken)` returning `ValueTask`|
|`ISyncRequest<T>`, `ISyncRequest`, `ISyncCommand`|`SendSync(request, cancellationToken)`|
|`IMultiRequest<T>`, `IMultiRequest`|`SendAllAsync(request, cancellationToken)`|
|`ISyncMultiRequest<T>`, `ISyncMultiRequest`|`SendAllSync(request, cancellationToken)`|
|`INotification`|Sequential `PublishAsync`; `Publish` is an alias returning `ValueTask`|
|`IStreamRequest<T>`|`StreamAsync(request, cancellationToken)` returning `IAsyncEnumerable<T>`|

Request and stream dispatch do not accept arbitrary runtime objects or a request stored only as its contract interface. Notification erasure is a separate supported path. Configure assembly discovery, behavior order and lifetimes with `AddZendiator`; use attributes as an alternative, not in the same compilation. Keep the runtime and generator package versions aligned.

## Performance

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

Means are after BDN overhead adjustment and outlier handling; retained N is 9–12. The [unrounded selected data](benchmarks/results/20261008-1c41d2b-summary.csv) separates Mean, confidence-interval Error, StdDev, N and allocation. The full run covered 360 cases across 74 comparison keys. One launch does not establish independent-run reproducibility, universal non-regression, or a controlled before/after improvement.

Handlers are deliberately lightweight. API shapes and DI registrations differ between libraries. ScopeK1 includes creation and disposal; FirstSend excludes them. Stream/relay figures include the complete benchmark operation, not one item. The relay behaviors synchronously preprocess and directly return the next enumerable. These results do not predict application startup, HTTP latency or asynchronous I/O. See the [benchmark instructions](benchmarks/README.md) for the fixture and reproduction conditions.

## AOT and trimming

`IsAotCompatible` is set, and generated code uses no reflection.
AOT/trim warnings are treated as errors, not suppressed.
A full native AOT link needs the native toolchain. For your own app referencing
the NuGet package, publish with `PublishAot` enabled (replace the example path):

```powershell
dotnet publish MyApp/MyApp.csproj -c Release -r win-x64 -p:PublishAot=true
```

CI uses package-based consumers for smoke tests and the Native AOT matrix, rather
than treating the in-repository ProjectReference sample as proof of package support.
See the [CI workflow](.github/workflows/ci.yml) for the checks and
[known limitations](docs/release/known-limitations.md) for release-specific evidence
and the open-generic/value-type boundary.

## Unsupported operations

Parallel publish, fire-and-forget, persistence/outbox, `Send(object)` for requests,
cycle detection, CodeFix, CodeLens, built-in `Result` pipeline mapping,
built-in logging, and Send input validation are not provided. Stream input validation
uses the explicitly registered synchronous validators described above.
Sequential `PublishAsync`, streams via `StreamAsync`, and your own
`Result` types as ordinary `TResponse` values are supported.

Consume each `ValueTask` returned by `PublishAsync` or `Publish` once.
To await the same publish more than once, call `AsTask()` once and reuse that
`Task`, without also consuming the original `ValueTask`. [CA2012](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2012)
can help detect incorrect consumption.

For migrating from MediatR, see [the migration guide](docs/migrating-from-mediatr.md).
