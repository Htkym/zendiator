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

The default mediator lifetime remains `Scoped`. Generated Handler and Behavior
registrations default to `Transient` when the mediator is Scoped; the mediator
still captures each instance on first use. Every service type is resolved
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

The default Transient registration changes how generated dependencies are shared
with other DI consumers. If a handler or behavior must be shared throughout its
scope, register it as Scoped before `AddZendiator()`, or set
`configuration.DependencyLifetime = ServiceLifetime.Scoped` to restore the old
generated registration lifetime for all dependencies. Attribute-based generated
registration also accepts `AddZendiator(ServiceLifetime.Scoped, ServiceLifetime.Scoped)`.

Register a transient mediator and resolve a new mediator when a fresh composition
is required. Merely making a handler transient no longer creates it per send.
Application state that changes per request belongs in the request or a suitable
scoped dependency, not in a handler assumed to be recreated on every send.
Handlers reused concurrently must support concurrent invocation.

Initialization is serialized per mediator; steady-state dispatch takes no cache
lock. A synchronous factory can reenter a different route on the same thread.
Factories must not block waiting for another thread to dispatch through the same
mediator during initialization. Failed activation is not cached and can be retried.

For ordinary closed request routes that all use one public reference-type handler,
the generator can use a typed field cache when there are no behaviors, other route
kinds, or explicit base-class declarations. Other configurations use the general
cache. Both follow the same lazy-resolution and disposal contract. The resolver
base type is generated infrastructure; applications should use the generated
constructor and dispatch APIs rather than depend on that base type.

## Ownership and disposal

DI owns Handler and Behavior disposal. The generated mediator does not implement
`IDisposable` and does not dispose cached dependencies. Keep its scope alive until
sends and stream enumeration finish. Sending after scope or root-provider disposal
is unsupported: a cached route may still call its former handler, while a route
that needs a new service may fail through DI. Disposal does not cancel or join
operations already running.

Enable `ValidateScopes` to catch scoped dependencies used from singleton mediators.
Because dependencies are lazy, some validation occurs on first dispatch rather
than during `ValidateOnBuild`.

Disposal follows the standard container. If a service throws while disposing, DI
may stop before disposing later services. Do not reuse a scope or mediator after
disposal starts, whether disposal succeeds or throws.

## Preview breaking changes

`BuildZendiatorServiceProvider`, `ZendiatorServiceProvider`,
`ZendiatorServiceProviderFactory`, and the provider registration marker were removed.
Replace custom provider construction with standard DI/host construction. Transient
handlers and behaviors are now lazy dependencies of a mediator instance, not
per-invocation factories. Generated dependencies of a Scoped mediator now
register as Transient by default. Code that resolves a handler separately from
DI will receive another instance. Set `DependencyLifetime` to Scoped or
pre-register the affected service as Scoped when scope-wide identity matters.

The generated mediator is registered directly as `IZendiator` with implementation
type `Zendiator`. The concrete type is no longer registered as a separate service,
and an independent `Zendiator` registration does not override `IZendiator`.
Replace concrete-type resolution or injection with `IZendiator`. For a custom
factory, change `AddScoped<Zendiator>(...)` to `AddScoped<IZendiator>(...)` before
`AddZendiator()`; the generated `TryAdd` registration preserves it. See the
[registration example](../README.md#usage). Apply the same change for Singleton or
Transient factories. Direct `new Zendiator(provider)` remains supported; DI owns
the dependencies it creates.

The generated mediator and its resolver base types no longer implement
`IDisposable`. The overloads accepting `ZendiatorRootLifetime` and that root
marker type were removed. Code that explicitly disposes or casts a mediator to
`IDisposable` must stop doing so and instead dispose its DI scope. Cached sends
after the scope or root provider has ended no longer have a guaranteed
`ObjectDisposedException`.

The generator package includes analyzer warnings ZEN0021–ZEN0023 for direct,
provable misuse of this lifetime contract. They cover explicit `IDisposable`
treatment, returning a mediator from a local `using` scope, and sending after
explicit scope disposal in the same method. They do not establish safety for
other control flow, asynchronous work, or mediators stored in fields.

## Measurements

Use the working-tree ProjectReference in `.local/benchmarks2/public-benchmarks` for
short comparisons. Warm dispatch and scope-startup cost must both be reported.
Published 0.1.0/0.1.1 measurements describe older architectures; do not present them
as measurements of this implementation. Short runs are exploratory, not proof of
an across-the-board fastest ranking.
