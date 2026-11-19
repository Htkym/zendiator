# Migrating from MediatR

[日本語](migrating-from-mediatr.ja.md)

If your application uses MediatR's `ISender`, `IPublisher` or `IMediator`, you can migrate one request and its callers at a time. The API comparison below covers MediatR 12 and 15. Zendiator 1.0.0-rc.1 targets .NET 10 and C# 14.

## Requests and handlers

Add these packages to the project that registers your handlers. Use a NuGet feed that carries the RC. A separate project that defines only request or notification types can reference `Zendiator.Abstractions` alone. Use the same version for `Zendiator` and `Zendiator.Abstractions` across the application.

```shell
dotnet add package Zendiator --version 1.0.0-rc.1
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
```

| MediatR | Zendiator |
|---|---|
| `IRequest<TResult>` | `Zendiator.IRequest<TResult>` |
| `IRequestHandler<TRequest, TResult>.Handle` returning `Task<TResult>` | `IRequestHandler<TRequest, TResult>.HandleAsync` returning `ValueTask<TResult>` |
| `IRequest` with no result | `IRequest` and `IRequestHandler<TRequest>.HandleAsync` returning `ValueTask` |
| `ISender.Send(request, token)` | `IZendiator.SendAsync(request, token)` |
| `IPublisher.Publish(notification, token)` | `IZendiator.PublishAsync(notification, token)` on a mediator configured for notifications |
| `CreateStream(request, token)` | `IZendiator.StreamAsync(request, token)` |

For a request with one handler, add `[Handler]` and select it in `AddZendiator<App>`. The example prints `Hello, Ada!`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Migration.Generated.App;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Migration;

[ZendiatorComposition("greetings")]
public sealed class App;

public sealed record Greet(string Name) : IRequest<string>;

[Handler]
public sealed class GreetHandler : IRequestHandler<Greet, string>
{
    public ValueTask<string> HandleAsync(Greet request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new($"Hello, {request.Name}!");
    }
}

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator<App>(c =>
            c.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler)));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Console.WriteLine(await mediator.SendAsync(new Greet("Ada")));
    }
}
```

Inject `Migration.Generated.App.IZendiator` into the class that used `ISender`. Its overloads take concrete request types. A variable typed as `object` or `IRequest<string>` must be narrowed to the request type before you can call `SendAsync`. If handlers live in another assembly, see [registering handlers](getting-started.md).

## Pipeline behaviors

If your `IPipelineBehavior` runs validation, transforms the request or response, or observes an exception, implement the corresponding hook and register it with `Stage.Hook<T>()`. For example, the [GettingStarted sample](../samples/Zendiator.Sample.GettingStarted/Program.cs) trims a name before dispatch and appends punctuation to the response. Move the outermost behavior to the first stage of `Pipeline`.

When your behavior can skip the handler on a cache hit or retry a call, use `IAsyncSendWrapper<TRequest,TResponse>` and `Stage.Wrapper<T>()`. It controls when to call `next.InvokeAsync(request, token)`. Await each call once and finish it before starting another. The [ComposedApp sample](../samples/Zendiator.Sample.ComposedApp/Program.cs) includes cache and retry wrappers.

## Notifications

To send requests and publish notifications through the same `IZendiator`, use the assembly-based `AddZendiator` overload. The following fragments use the request, notification and handlers from [Sample.Application](../samples/Zendiator.Sample.Application/DependencyInjection.cs) and [Sample.Contracts](../samples/Zendiator.Sample.Contracts/). The first fragment belongs in service registration. The second runs inside a DI scope and takes the caller's cancellation token.

```csharp
services.AddZendiator(c =>
{
    c.Namespace = "Zendiator.Sample.Application.Generated";
    c.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>();
    c.RegisterServicesFromAssemblyContaining<ContractsAssemblyMarker>();
});
```

```csharp
var mediator = scope.ServiceProvider.GetRequiredService<
    global::Zendiator.Sample.Application.Generated.IZendiator>();
var id = Guid.NewGuid();
await mediator.SendAsync(new PurgeHouseholdCommand(id), token);
await mediator.PublishAsync(new HouseholdPurged(id), token);
```

A subscriber implements `INotificationHandler<TNotification>.HandleAsync` and returns `ValueTask`. `PublishAsync` awaits subscribers in order and stops at the first failure. Use `[HandlerOrder(Order = ...)]` when order matters. For a known notification with no subscribers, publishing completes successfully. If your MediatR application uses parallel publishing, account for the change in ordering and failure handling.

The named `AddZendiator<App>` form in the request example selects Send and Stream routes. Passing `DispatchFamily.Notification` to its `Handlers` or `Pipeline` produces [ZEN0029](diagnostics/configuration.md#zen0029).

## Streams

An iterator-based handler can keep its `IAsyncEnumerable<TItem>` body. Implement `IStreamHandler<TRequest,TItem>.Handle(request, token)`, add `[Handler]`, and select the handler with `DispatchFamily.Stream`. In a named composition, declare `Pipeline<TRequest>(DispatchFamily.Stream)` even when the stream has no middleware.

Consume the result with `await foreach` and forward cancellation. When API and enumeration tokens come from different callers, pass the first to `StreamAsync` and the second with `WithCancellation`. Keep the DI scope alive until enumeration and disposal finish, including when an HTTP response continues reading after its endpoint returns. If your producer already opens a named cursor or you transform stream items, see [Streams](streaming.md).

## Scope, exceptions and cancellation

Zendiator registers the mediator as Scoped by default. The same mediator reuses captured handlers and hooks, including Transient services. A retry or second send in the same scope may use the same instance. On eligible closed Send routes with hooks, construction of downstream handlers can occur before the first hook runs, even if that hook fails. Check constructors with side effects and avoid storing request-local state on shared handlers.

Keep both the call to `SendAsync` and its `await` inside the `try` if you handle failures. Dependency resolution, cancellation or a synchronous callback can throw before a `ValueTask` is returned. Consume each returned `ValueTask` once. When migrating, exercise the paths your application uses, including a canceled request, a failed hook, and a stream consumer that stops after the first item. [Compatibility and lifetime](release/known-limitations.md)
