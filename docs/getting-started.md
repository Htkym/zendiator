# Register and send a request

[日本語](getting-started.ja.md)

Zendiator targets .NET 10 and C# 14. The [GettingStarted sample](../samples/Zendiator.Sample.GettingStarted/Program.cs) includes a synchronous calculation, a greeting with two hooks and a Stream. With a NuGet feed that provides `1.0.0-rc.1`, run it from the repository root.

```shell
dotnet run --project samples/Zendiator.Sample.GettingStarted -c Release
```

```text
Send: 42
SendAsync: Hello, Ada!
StreamAsync: 20, 40
```

## Define a request and its handler

In an application, reference `Zendiator` and `Microsoft.Extensions.DependencyInjection`. A project that shares only request types can reference `Zendiator.Abstractions`. Use the same version for both Zendiator packages.

```csharp
using Zendiator;

namespace GettingStarted;

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

The `App` marker names this group of registrations. In the `GettingStarted` namespace it gives you the `GettingStarted.Generated.App.IZendiator` interface. The string in the attribute identifies the composition.

## Register the handler and call it

In your application startup, import `GettingStarted`, `GettingStarted.Generated.App`, `Microsoft.Extensions.DependencyInjection`, `Zendiator` and `Zendiator.DependencyInjection`. The following code returns `Hello, Ada`.

```csharp
var services = new ServiceCollection();
services.AddZendiator<App>(c =>
{
    c.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler));
});

await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var greeting = await mediator.SendAsync(new Greet("Ada"));
```

Call `AddZendiator` with the configuration lambda at the registration site. If a host expects a delegate, wrap that direct call in your own method or lambda. A conditional call such as `services?.AddZendiator<App>(...)` skips registration when the receiver is null. [Configuration diagnostics](diagnostics/configuration.md#zen0020)

## Add work before or after the handler

To trim the incoming name and append an exclamation mark to the result, add this line inside the registration lambda after `Handlers<Greet>` and use the hooks below.

```csharp
c.Pipeline<Greet>(DispatchFamily.SendAsync,
    Stage.Hook<TrimName>(), Stage.Hook<AddExclamation>());
```

```csharp
public sealed class TrimName : ISendRequestTransform<Greet>
{
    public Greet Before(Greet request, CancellationToken token) =>
        request with { Name = request.Name.Trim() };
}

public sealed class AddExclamation : ISendResponseTransform<Greet, string>
{
    public string After(Greet request, string response, CancellationToken token) =>
        response + "!";
}
```

`SendAsync(new Greet(" Ada "))` now returns `Hello, Ada!`. Before hooks run in registration order. After hooks run in reverse order. Use a wrapper when the stage needs to decide whether to call the next stage, for example to return a cached response.

## Choose how to dispatch

| What the caller needs | Registration in a named composition | Call |
|---|---|---|
| A synchronous result | `DispatchFamily.SendSync` | `mediator.Send(request)` |
| An asynchronous result or completion | `DispatchFamily.SendAsync` | `await mediator.SendAsync(request)` |
| Results from several handlers | `SendAllSync` or `SendAllAsync`, with the handlers listed in order | `SendAll` or `await SendAllAsync` |
| Items as they become available | `DispatchFamily.Stream`, with a handler and an explicit pipeline | `await foreach` over `StreamAsync` |

For Stream input, filtering and cleanup, see [Stream handlers](streaming.md).

For open generic requests, use `typeof(Lookup<>)` and `typeof(LookupHandler<>)` in the `Handlers` call. Call the mediator with a concrete request such as `new Lookup<Product>("p-7")`. [Generic query sample](../samples/Zendiator.Sample.GenericLookup/Program.cs) · [Multiple handlers and wrappers](../samples/Zendiator.Sample.ComposedApp/Program.cs)

## Send a request and publish a notification

Use the non-generic `AddZendiator` to register the assemblies that contain your message types, handlers and subscribers. Each subscriber implements `INotificationHandler<TNotification>`. These types come from the [sample application](../samples/Zendiator.Sample.Application/DependencyInjection.cs) and its contracts project.

```csharp
services.AddZendiator(configuration =>
{
    configuration.Namespace = "Zendiator.Sample.Application.Generated";
    configuration.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>();
    configuration.RegisterServicesFromAssemblyContaining<ContractsAssemblyMarker>();
});
```

Resolve the mediator from the scope. After the command completes, send the notification to its subscribers.

```csharp
var mediator = scope.ServiceProvider.GetRequiredService<
    global::Zendiator.Sample.Application.Generated.IZendiator>();
var id = Guid.NewGuid();
await mediator.SendAsync(new PurgeHouseholdCommand(id));
await mediator.PublishAsync(new HouseholdPurged(id));
```

`PublishAsync` calls subscribers in the same process, waits for each to finish, and stops at the first failure. Named compositions such as `AddZendiator<App>` support Send and Stream. Passing `DispatchFamily.Notification` to their `Handlers` or `Pipeline` methods reports ZEN0029.

## Keep the scope alive

The default mediator is scoped. A mediator can reuse the handler it resolved on an earlier call, including a Transient handler. Keep per-request mutable state in the request or an operation-owned session. Use a Scoped or Singleton registration before `AddZendiator` if several routes must share the same receiver. Await dispatch and Stream disposal before releasing the scope.

Await each returned `ValueTask` once. A plain `SendAsync` can throw during dependency resolution or a synchronous callback, before returning the operation. Put the call and its `await` in the same `try` block. [Dependency lifetime and failure handling](release/known-limitations.md)
