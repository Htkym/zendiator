# Zendiator

[日本語](README.ja.md)

Zendiator is a fast, source-generated mediator for .NET 10. **Sending a request through five synchronous Before hooks took 6.4–6.7 ns on average with zero allocation per call.** The mediator and request were ready before timing began. Define a handler and call `SendAsync`. The source generator connects the request to the handler at build time.

## Performance

The comparison covered requests, notifications, streams and hooks. Zendiator had the lowest mean time on all 14 representative workloads in both execution orders. Here are three results from the forward run on Windows x64, .NET 10.0.14, revision `0d53c53`.

| Workload | Zendiator | Fastest other library | Zendiator allocation per call |
|---|---:|---:|---:|
| Send a request through five synchronous Before hooks | 6.71 ns | Immediate.Handlers · 20.95 ns | 0 B |
| Notify 16 synchronous subscribers | 140.12 ns | Mediator · 229.14 ns | 0 B |
| Read one item from a synchronous stream and dispose it, five middleware stages | 317.31 ns | Immediate.Handlers · 385.47 ns | 976 B |

These measurements used a commit from before the `1.0.0` release. The asynchronous stream that stops after one item was the only workload whose confidence interval overlapped a competitor’s in both runs. [All 14 results, allocations and measurement conditions](benchmarks/README.md)

## Install

Add the following NuGet packages to a project that uses .NET 10 and C# 14.

```shell
dotnet add package Zendiator --version 1.0.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
```

The `Zendiator` package includes the source generator. A project that only defines shared requests and handler contracts can reference `Zendiator.Abstractions` alone. Keep both packages at the same version across the application.

## Send your first request

Put this code in `Program.cs` in a console app. It prints `Hello, Ada`.

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
        services.AddZendiator<App>(c =>
            c.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler)));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Console.WriteLine(await mediator.SendAsync(new Greet("Ada")));
    }
}

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

The generated `IZendiator` exposes a typed method for each request. Register a `Pipeline` when you want hooks or middleware to run in a particular order. Keep the DI scope alive until the request finishes and, for streams, until the enumerator has been disposed. [Getting started](docs/getting-started.md) · [Runnable sample](samples/Zendiator.Sample.GettingStarted/)

## Choose a dispatch method

| To… | Use |
|---|---|
| Return one result | `Send` or `SendAsync` |
| Collect results from multiple handlers | `SendAll` or `SendAllAsync` |
| Notify subscribers in order | `PublishAsync` with assembly-based registration |
| Read a stream item by item | `await foreach` over `StreamAsync` |

Use a named composition like `AddZendiator<App>` for an explicit Send or Stream pipeline. To send requests and publish notifications from the same mediator, register the message and handler assemblies with `AddZendiator`. [Send and Publish example](docs/getting-started.md#send-a-request-and-publish-a-notification)

## Guides

- [Stream handlers, cancellation and early exit](docs/streaming.md)
- [Move from MediatR](docs/migrating-from-mediatr.md) · [Upgrade Zendiator](docs/upgrading.md)
- [DI lifetimes, compatibility and Native AOT support](docs/release/known-limitations.md)
- [Configuration diagnostics](docs/diagnostics/configuration.md) · [IDE fixes for missing handler attributes](src/Zendiator.CodeFixes/README.md)

[Release notes](docs/release/1.0.0-release-notes.md) · [MIT license](LICENSE)
