# Migrate to 0.6.0

## Handler marker

Replace `[ZendiatorHandler]` with `[Handler]` from namespace `Zendiator`.
The public type is now `Zendiator.HandlerAttribute`; the old type is removed.
Use `[global::Zendiator.HandlerAttribute]` when a local name is ambiguous.
Keep runtime, Abstractions, generator and optional CodeFix packages at 0.6.0,
and rebuild consumers. This does not rename your handler classes or methods.

## Standard typed Stream middleware

`IStreamMiddleware<TRequest,TItem>` now exposes `OpenAsync<TNext>` directly. A separate
`IStructStreamMiddleware` declaration is no longer required. The compatibility name remains available.

```csharp
public sealed class Relay<TRequest, TItem> : IStreamMiddleware<TRequest, TItem>
{
    public ValueTask<IAsyncEnumerator<TItem>> OpenAsync<TNext>(
        TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamUpstream<TRequest, TItem>
        => next.OpenAsync(request, cancellationToken);
}
```

The generator selects the typed path for an accessible sealed class with a public, ordinary,
nonvirtual generic implementation declared on that class, with the exact interface constraints.
There is no composition switch. This is automatic selection of a proved implementation shape,
not inspection or rewriting of an arbitrary method body.

For a middleware retaining the port, make its cursor generic in `TNext` and store a `TNext` field.
Casting the port to `IStreamUpstream` or `object` can box it and lose the allocation benefit.
A relay should return the upstream cursor directly; do not add a cursor merely to adapt the contract.
The sample logging middleware and standard benchmark Stream/Relay wrappers use the typed overload.
The benchmark's Work.Enter/Work.Relay, producer, cancellation, Task.Yield and cleanup are unchanged.

## Existing implementations and other receiver shapes

Existing implementations of the nongeneric overload continue to work. They retain the old generated
interface/class-port path, so they are not advertised as receiving the struct allocation saving.
Add the generic overload, or migrate its body to that overload, to use the typed standard path.
If both overloads are implemented, they must have equivalent behavior.

Default interface adapters connect legacy callers to typed-only implementations and typed callers
to legacy-only implementations. Neither overload may be left unimplemented: the generator reports
ZEN0025 for an empty middleware, and direct interface invocation fails instead of recursing.

Explicit, inherited, virtual, nonsealed, value receivers and ambiguous generic overloads retain the
interface dispatch path, preserving runtime DI overrides. A typed-only implementation on that path
uses the default adapter and generic interface dispatch; it is not the concrete fast path. The port
allocation saving is promised only for the eligible generated path. Existing DI service keys,
lifetime, borrowed ownership and disposal remain unchanged.

The sample's old concrete nongeneric method has been replaced by its generic method. Callers that
invoke a concrete sample middleware with an interface-typed upstream must use the middleware
interface adapter or migrate to the generic signature. No external middleware rewrite is performed.

## Semantics and Native AOT

Port copies share one operation owner. Default ports are invalid. Only normal EOF plus completed
cleanup permits reopening; early disposal, faults and root closure retain their existing rules.
Private generated port types do not enter the public Mediator facade. The public stream enumerable
and typed terminal signatures do not change.

The concrete generated path keeps its upstream port in a value type. Generic interface adapters
and runtime-created generic DI closures are different execution paths; support for one shape does
not imply support for every closure. See [Native AOT limitations](release/known-limitations.md)
for the supported deployment scope. Value-type open-generic DI closures remain limited.

## Eager dependencies are a separate choice

Eager dependency activation is explicit through `EagerDependencies`. Static reference or even an unconditional handler
call does not prove that a caller will dispatch after resolving the Mediator. Earlier stages may
short-circuit, throw or cancel; DI registrations and factories can change at runtime. Moving activation
into the Mediator constructor can run side effects, throw earlier, resolve unused services or change
scoped/transient activation order. A usage heuristic cannot safely choose those semantics.

Without explicit eager selection, existing generated getters resolve a dependency at its actual first
use and cache it within the existing ownership rules. This is the safe automatic behavior already in
place. Prefetching at first dispatch would still resolve services before short-circuit/branch decisions;
prefetching only at the exact call site is the current getter behavior. Dependencies are not automatically made eager based on inferred usage.
