# Compatibility, lifetimes and supported deployments

`1.0.0` targets .NET 10 and C# 14. Reference matching versions of `Zendiator` and `Zendiator.Abstractions` and rebuild the consuming projects when you upgrade. [Release notes](1.0.0-release-notes.md) · [Upgrade guide](../upgrading.md)

## API and compatibility boundaries

From 1.0.0 onward, incompatible changes to a documented public API or its documented behavior require a new major version. This covers the request, handler, hook, middleware and configuration contracts, generated DI registration methods, and typed calls on the generated `IZendiator` and `Zendiator` facade. Applications can also rely on dispatch order, dependency reuse, cancellation and ownership as described here.

Use the facade to dispatch and the public stage contracts to extend it. Generated type names, base classes, storage layout, call-site interceptors and runtime helpers used by generated code can change when you rebuild. The optional `[GeneratePooledProducer]` feature is experimental. Its accepted method bodies and diagnostics may change within 1.x. [Pooled producer methods](../pooled-producer.md)

## Dependency capture and lifetime

By default the mediator is Scoped and its registered dependencies are Transient. The mediator reuses the dependencies it has resolved across calls, including services produced by an implementation factory. Another route, another mediator or a separate resolution from DI may receive a different Transient instance. Register a service as Scoped or Singleton before `AddZendiator` when its identity must be shared. DI owns disposal of borrowed services.

On eligible closed `SendAsync` routes, the first dispatch resolves the handler and hooks together after the initial request and cancellation checks and before the first callback. An outer hook can fail after downstream receivers have already been constructed. This behavior includes routes with only synchronous Before hooks when the handler and hooks are borrowed reference types, there are at least two distinct receiver types, and the route has no wrapper or session factory. On these routes, resolution and Before hooks run during the call, which returns the handler's original `ValueTask`.

A receiver used only by such groups is resolved through the original provider when requested via the mediator's explicit `IServiceProvider`. A type also used by a direct route can have a separate instance captured by that route. When first calls run concurrently, DI can construct more than one Transient. Successful callers use the instance retained for the route. Each failing resolution reports its own failure, and a later call can retry after a failure or a null result.

For supported closed Send routes, `EagerDependencies` on the composition marker selects services for constructor injection when DI resolves the mediator. Selecting a receiver of a synchronous-Before-only route keeps that route's per-type capture and uses the injected service. Unlisted receivers follow their usual resolution timing. The default `ClosedServiceCacheMode.Auto` stores closed dependencies inline. A composition with open generic services, more than two closed dependency types and no eager dependencies reserves an inline slot for the first closed type requested and shares the open-generic cache for other types. You can select `Inline` to keep a field for every closed service, or `LazyBlocks` to allocate groups of fields on demand. All modes preserve service lifetimes.

Use the generated registration even with a custom provider. The provider must be able to resolve registered receiver groups and their constructor dependencies. `ValidateOnBuild` checks the dependency graphs that DI can inspect. Exercise your open generic routes and service factories as well. Use `ValidateScopes` to catch invalid dependencies, such as a Singleton mediator capturing a Scoped service.

Keep the owning scope alive until dispatch, enumeration and awaited cleanup finish. Disposing the scope neither cancels nor waits for pending work. A shared handler, hook or sink must support the concurrency your application allows.

## Completion, cancellation and failures

Await each returned `ValueTask` once. A plain `SendAsync` can throw during the call before it returns the operation. Put both the call and the await inside the same `try` block.

Before hooks run in registration order. After hooks unwind in reverse. On asynchronous hook routes, an error callback receives the current exception and can replace it. A failing cleanup takes precedence over a prior failure. A stage must finish using its continuation within the operation and call it sequentially.

Cancellation is cooperative. Pass the caller's token to dispatch and to the I/O your handler starts. Keep operation state needed after an await in the request, cursor or session. Synchronous callbacks can change ambient values such as `AsyncLocal`, culture or synchronization context. Which callbacks observe a change can depend on whether an await suspends.

## Stream ownership and use

`StreamAsync` returns an `IAsyncEnumerable<T>`. Each enumeration needs its own cursor, even when the mediator shares handler instances. A flat iterator route with two noncancelable tokens can acquire the cursor at `GetAsyncEnumerator`. Other routes initialize on their first move. Use `await foreach` or await `DisposeAsync` on a manually acquired cursor, including after early exit and on error. [Input contracts and cleanup](../streaming.md#choose-an-input-contract)

On guarded routes, `Current` is valid after a successful move until the next accepted operation. Overlapping moves or a disposal during a pending move fail. During disposal, new moves fail and repeated disposal waits for the same result. Owned cursors and sessions are disposed once in reverse order. An upstream can reopen only after normal end of input and successful cleanup. A flat route that exposes the producer's cursor directly follows the producer's rules.

A sink passed to `StreamSource(...).ConsumeAsync(...)` belongs to the caller. Zendiator awaits each item callback, and a `false` result stops consumption normally. Each operation can hold references to items while it awaits input or a callback. Keep the DI scope alive through HTTP serialization when the result is returned from an endpoint. [Stream guide](../streaming.md)

## Native AOT and dispatch boundaries

Windows x64 Native AOT execution has been verified for package consumers using DI registration or assembly attributes, registered reference-type generic closures, typed Stream middleware with a struct cursor and upstream, and typed sink consumption. Coverage also includes explicit eager dependencies, a closed Send route with synchronous Before hooks, and the opt-in pooled producer. Use the RID and route types you deploy for your own Native AOT check. Other RIDs have not been verified here.

Under Native AOT, DI can fail to close open generic Send or Stream handlers over value-type arguments. This limitation is separate from using a struct as a Stream cursor or upstream. For Send and Stream, dispatch a statically typed request. Runtime-object dispatch is unsupported for these families. Notifications can use type erasure for registered closed notification types. `PublishAsync` awaits subscribers sequentially and stops at the first failure. [Request and notification example](../getting-started.md#send-a-request-and-publish-a-notification)
