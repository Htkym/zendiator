# Upgrading Zendiator to 1.0.1

[日本語](upgrading.ja.md)

If you use 1.0.0, update your Zendiator packages together and rebuild. The generated mediator now defaults to `ClosedServiceCacheMode.Auto`. Set `ClosedServiceCache` to `ClosedServiceCacheMode.Inline` on the composition marker if you need the previous cache layout. [Cache modes](release/known-limitations.md#dependency-capture-and-lifetime)

If you use 0.7.0, keep your request and handler definitions. The DI lifetime rules are the same in 0.7.0 and 1.0.0. The changes you may see when upgrading are a build error for mismatched packages, a diagnostic for registration delegates, and a fix for Stream middleware that returns a decorated cursor.

## Update the packages together

Update the project that registers Zendiator, then update any project that explicitly references `Zendiator.Abstractions`. Replace the paths below with your project paths. If you manage versions centrally, update both entries there.

```shell
dotnet add App/App.csproj package Zendiator --version 1.0.1
dotnet add Contracts/Contracts.csproj package Zendiator.Abstractions --version 1.0.1
dotnet build App/App.csproj
```

An application referencing only `Zendiator` receives Abstractions as a dependency. In an ordinary PackageReference build, resolving different versions of the two packages reports an error. Align the references across the solution and restore again. If you installed the optional `Zendiator.CodeFixes` package, update it to the matching version and keep `PrivateAssets="all"`.

## DI registration

Existing calls to `services.AddZendiator<App>(...)` continue to work. A conditional call such as `getServices()?.AddZendiator<App>(...)` evaluates the receiver once and skips registration when it returns null. The fully qualified static call is also supported.

If you assigned `ZendiatorServiceCollectionExtensions.AddZendiator<App>` directly to a delegate, [ZEN0020](diagnostics/configuration.md#zen0020) now points to that assignment at build time. Pass a lambda or a helper method that makes the direct registration call. This example uses the request and handler from the [MediatR migration example](migrating-from-mediatr.md).

```csharp
Action<IServiceCollection> register = collection =>
    collection.AddZendiator<App>(c =>
        c.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler)));

register(services);
```

## Streams and disposal

Keep calling `mediator.StreamAsync(request, token)` and consuming the result with `await foreach`. Your middleware can return `next.OpenAsync(request, token)` directly, store the operation in a local variable before returning, or return a different cursor. Zendiator checks the actual cursor and effective token to decide whether it can read directly from upstream. When a middleware returns a decorator, the consumer observes its item handling and disposal.

Keep the DI scope alive until enumeration and disposal finish, including when you stop after the first item. When operating an enumerator manually, await a pending `MoveNextAsync` before calling `DisposeAsync`. If middleware reopens an upstream, the previous cursor must first reach normal end of stream and finish cleanup. [Streams](streaming.md)

## When updating from an earlier 0.x release

| Version you use | What to check |
|---|---|
| 0.6.x or earlier | Since 0.7.0, eligible closed `SendAsync` routes with synchronous Before hooks construct all their receivers before the first hook runs. A failing outer hook can leave a downstream handler constructed but unused. Check constructors with side effects. Different routes can capture different Transients. Register a service as Scoped or Singleton if it needs a shared identity. |
| 0.5.x | Replace `[ZendiatorHandler]` with `[Handler]`. Standard `IStreamMiddleware<TRequest,TItem>` now exposes typed `OpenAsync<TNext>`. Existing implementations of the interface-upstream overload still work. If you call a typed-only implementation directly with an interface-typed upstream, call it through `IStreamMiddleware<TRequest,TItem>` to use the adapter. |
| 0.4.x or earlier | For Streams, implement `IStreamHandler<TRequest,TItem>.Handle` to return an async enumerable, or `IStreamSourceHandler<TRequest,TItem,TCursor>.OpenAsync` for a named cursor. Declare your handler and stages explicitly for a named composition. To run preprocessing before the source opens, use `IAsyncStreamBefore<TRequest>` and register it with `StreamBefore`. |

Validation that used to run synchronously when calling `StreamAsync` in 0.4.x can now run on the first `MoveNextAsync` when using `StreamBefore`. Put error handling around enumeration as well as the call. Send handlers that return `ValueTask` can fail synchronously during resolution or a callback, so keep the call and its `await` together inside `try`. A mediator reuses captured receivers, including Transients. Check repeated sends and retries if an older application expects a new handler for every call. [Compatibility and lifetimes](release/known-limitations.md)
