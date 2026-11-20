# Zendiator

[日本語](README.ja.md)

Zendiator is an in-process mediator for .NET 10 and C# 14. Its source generator connects requests to handlers at build time and generates registration code for `Microsoft.Extensions.DependencyInjection`. Use `SendAsync` to send a request, `PublishAsync` to notify subscribers and `StreamAsync` to read an asynchronous stream.

[Send your first request](#send-your-first-request) · [Using Zendiator as a MediatR alternative](docs/migrating-from-mediatr.md) · [Native AOT configurations verified on Windows x64](docs/release/known-limitations.md#native-aot-and-dispatch-boundaries)

## Performance

We measured Release builds on Windows 11 x64 with .NET 10.0.14 and BenchmarkDotNet 0.15.8. Each cell shows **time / bytes allocated per operation**, averaged over the forward and reverse runs.

| Workload | Zendiator 1.0.1 | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 1.84 ns / 0 B | 11.20 ns / 0 B | 57.30 ns / 200 B | 58.15 ns / 64 B | 39.16 ns / 0 B | 40.85 ns / 0 B |
| Send, 5 stages | 8.29 ns / 0 B | 23.26 ns / 0 B | 201.46 ns / 1,016 B | 129.84 ns / 544 B | 47.19 ns / 0 B | 209.79 ns / 0 B |
| Resolve again in the same scope and send, 0 stages | 32.23 ns / 0 B | 49.77 ns / 0 B | 80.44 ns / 200 B | 97.64 ns / 64 B | 70.73 ns / 0 B | 82.08 ns / 0 B |
| Resolve again in the same scope and send, 5 stages | 41.24 ns / 0 B | 56.67 ns / 0 B | 196.21 ns / 1,016 B | 146.43 ns / 544 B | 80.05 ns / 0 B | 224.51 ns / 0 B |
| Create a scope, send once and dispose, 5 stages | 126.58 ns / 560 B | 152.24 ns / 568 B | 260.63 ns / 1,400 B | 219.39 ns / 920 B | 652.91 ns / 1,712 B | 384.20 ns / 856 B |
| Notify 16 synchronous subscribers | 144.53 ns / 0 B | N/A | 1,207.51 ns / 4,784 B | 229.39 ns / 0 B | 209.52 ns / 0 B | 1,005.43 ns / 0 B |
| Stream, all 1,024 items, 5 stages, sync | 15.013 µs / 928 B | 107.542 µs / 1,104 B | 214.211 µs / 3,592.5 B | 108.836 µs / 1,688.5 B | 106.151 µs / 1,104.5 B | 127.107 µs / 1,808 B |
| Stream, all 1,024 items, 5 stages, async | 166.127 µs / 1,586 B | 313.403 µs / 1,755 B | 613.929 µs / 4,726 B | 320.580 µs / 2,339.5 B | 314.207 µs / 1,756 B | 353.049 µs / 2,539 B |
| Stream, 1 of 16 items, 5 stages, sync | 306.31 ns / 928 B | 347.98 ns / 1,104 B | 977.11 ns / 3,592 B | 450.96 ns / 1,688 B | 377.69 ns / 1,104 B | 744.31 ns / 1,808 B |
| Stream, 1 of 16 items, 5 stages, async | 2.105 µs / 1,256 B | 2.436 µs / 1,752 B | 4.843 µs / 4,720 B | 2.735 µs / 2,336 B | 2.426 µs / 1,752 B | 3.022 µs / 2,536 B |
| Short-circuit at first of 3 stages, warm | 5.64 ns / 0 B | 6.48 ns / 0 B | 116.34 ns / 704 B | 75.54 ns / 496 B | 41.13 ns / 0 B | 77.22 ns / 384 B |
| Async hooks, successful first call | 1.023 µs / 448 B | 1.711 µs / 504 B | 1.560 µs / 1,352 B | 1.349 µs / 808 B | 1.338 µs / 560 B | 2.154 µs / 856 B |
| Async hooks, successful warm call | 41.48 ns / 0 B | 61.46 ns / 0 B | 780.85 ns / 944 B | 612.56 ns / 2,900.5 B | 82.21 ns / 0 B | 600.23 ns / 480 B |
| Async hooks, outer hook fails on first call | 5.492 µs / 1,712 B | 10.858 µs / 3,808 B | 10.142 µs / 3,416 B | 9.032 µs / 3,216 B | 8.069 µs / 2,968 B | 13.138 µs / 4,872 B |

[Benchmark setup and reproduction steps](benchmarks/README.md) · [Raw measurements (CSV)](benchmarks/results/f2973af-representative-summary.csv)

## Install

Add the following NuGet packages to a project that uses .NET 10 and C# 14.

```shell
dotnet add package Zendiator --version 1.0.1
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

[Release notes](docs/release/1.0.1-release-notes.md) · [MIT license](LICENSE)
