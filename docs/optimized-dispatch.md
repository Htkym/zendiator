# Construction and dispatch lifetime

Zendiator has one dispatch path. It lazily captures each Handler and Behavior from
standard DI on first use, then reuses that dependency for the mediator instance.
There is no optimized-mode switch, custom provider, or provider factory.

## Setup

Use the existing generated registration or the DI configuration entry point:

```csharp
services.AddZendiator();
using var provider = services.BuildServiceProvider();
```

In ASP.NET Core, register with `builder.Services.AddZendiator()` and construct the
application with the normal `builder.Build()`. Do not call `BuildServiceProvider`
during host registration. Inject the generated `IZendiator` into consumers.
Normal injected `IServiceScopeFactory` and `CreateAsyncScope()` work without adapters.

This follows the standard service-registration approach used by
[Mediator](https://github.com/martinothamar/Mediator),
[Immediate.Handlers](https://github.com/ImmediatePlatform/Immediate.Handlers), and
[DispatchR](https://github.com/hasanxdev/DispatchR). Their execution and default
lifetime contracts differ; the setup similarity is not a claim of equivalence.

## Lifetime contract

The default mediator lifetime remains `Scoped`. Every service type is resolved
only when its pipeline node is first reached, including notification subscribers,
synchronous handlers, and stream handlers. Short-circuiting does not construct
unused downstream dependencies. Retrying a continuation invokes it again with the
new request/token but reuses the dependency already captured by that mediator.

`TryAdd` preserves pre-registrations. Standard DI chooses the instance, including
custom factories and closed/open generic overrides. Its descriptor lifetime does
not control how often a mediator resolves that dependency:

| Registration | Within one mediator | Across mediator instances |
| --- | --- | --- |
| Singleton dependency | Captured once | Shared by DI |
| Scoped dependency | Captured once | Shared within its DI scope |
| Transient dependency | Captured once | Resolved anew for each mediator that uses it |

Register a transient mediator and resolve a new mediator when a fresh composition
is required. Merely making a handler transient no longer creates it per send.
Application state that changes per request belongs in the request or a suitable
scoped dependency, not in a handler assumed to be recreated on every send.
Handlers reused concurrently must support concurrent invocation.

Initialization is serialized per mediator; steady-state dispatch takes no cache
lock. A synchronous factory can reenter a different route on the same thread.
Factories must not block waiting for another thread to dispatch through the same
mediator during initialization. Failed activation is not cached and can be retried.

## Ownership and disposal

DI owns Handler and Behavior disposal. The generated mediator implements
`IDisposable` only to invalidate its cache; it does not dispose those dependencies.
Normal mediator/scope disposal and normal root-provider disposal reject subsequent
service resolution, including warmed routes. Already-running operations are not
canceled or joined by disposal. Keep the scope alive until sends and stream
enumeration finish. Let DI construct and dispose the mediator.

Enable `ValidateScopes` to catch scoped dependencies used from singleton mediators.
Because dependencies are lazy, some validation occurs on first dispatch rather
than during `ValidateOnBuild`.

Disposal follows the standard container. If a service throws while disposing, DI
may stop before the mediator or root marker is notified. Do not reuse a scope or
mediator after disposal starts, whether disposal succeeds or throws. There is no
custom wrapper promising invalidation before a failing cleanup.

## Preview breaking changes

`BuildZendiatorServiceProvider`, `ZendiatorServiceProvider`,
`ZendiatorServiceProviderFactory`, and the provider registration marker were removed.
Replace custom provider construction with standard DI/host construction. Transient
handlers and behaviors are now lazy dependencies of a mediator instance, not
per-invocation factories. There is no compatibility mode.

## Measurements

Use the working-tree ProjectReference in `.local/benchmarks2/public-benchmarks` for
short comparisons. Warm dispatch and scope-startup cost must both be reported.
Published 0.1.0/0.1.1 measurements describe older architectures; do not present them
as measurements of this implementation. Short runs are exploratory, not proof of
an across-the-board fastest ranking.
