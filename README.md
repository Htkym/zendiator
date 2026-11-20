# Zendiator

[日本語](README.ja.md)

Zendiator is a fast, source-generated mediator for .NET 10. **Sending a request through five synchronous Before hooks took 7.01–9.58 ns on average and allocated 0 B per call.** The mediator and request were ready before timing began. Define a handler and call `SendAsync`. The source generator connects the request to the handler at build time.

## Performance

The measurements used Windows x64, .NET 10.0.14 and Zendiator revision `f2973af`. Each cell gives the **mean time / bytes allocated per operation**. The two runs measured the same 83 combinations in opposite orders. Immediate.Handlers has no corresponding notification workload, shown as `N/A`.

Zendiator had the lowest mean time in all 14 workloads in both orders. It allocated fewer bytes than every competitor in 7 workloads and tied at 0 B in the other 7.

### Forward order

| Workload | Zendiator | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 2.04 ns / 0 B | 13.19 ns / 0 B | 70.45 ns / 200 B | 70.59 ns / 64 B | 45.65 ns / 0 B | 44.90 ns / 0 B |
| Send, 5 stages | 9.58 ns / 0 B | 26.84 ns / 0 B | 239.07 ns / 1,016 B | 153.75 ns / 544 B | 52.36 ns / 0 B | 235.80 ns / 0 B |
| Resolve again in the same scope and send, 0 stages | 33.52 ns / 0 B | 54.41 ns / 0 B | 82.66 ns / 200 B | 114.87 ns / 64 B | 79.80 ns / 0 B | 86.41 ns / 0 B |
| Resolve again in the same scope and send, 5 stages | 35.75 ns / 0 B | 57.85 ns / 0 B | 197.13 ns / 1,016 B | 147.42 ns / 544 B | 83.03 ns / 0 B | 226.87 ns / 0 B |
| Create a scope, send once and dispose, 5 stages | 131.93 ns / 560 B | 155.14 ns / 568 B | 268.97 ns / 1,400 B | 227.14 ns / 920 B | 774.62 ns / 1,712 B | 389.28 ns / 856 B |
| Notify 16 synchronous subscribers | 143.94 ns / 0 B | N/A | 1,249.78 ns / 4,784 B | 230.08 ns / 0 B | 209.27 ns / 0 B | 1,006.80 ns / 0 B |
| Stream, all 1,024 items, 5 stages, sync | 15.242 µs / 928 B | 108.246 µs / 1,104 B | 216.464 µs / 3,593 B | 108.292 µs / 1,688 B | 106.468 µs / 1,104 B | 127.324 µs / 1,808 B |
| Stream, all 1,024 items, 5 stages, async | 167.413 µs / 1,586 B | 311.844 µs / 1,755 B | 624.129 µs / 4,726 B | 329.834 µs / 2,340 B | 316.544 µs / 1,756 B | 352.939 µs / 2,539 B |
| Stream, 1 of 16 items, 5 stages, sync | 306.07 ns / 928 B | 348.98 ns / 1,104 B | 951.59 ns / 3,592 B | 453.85 ns / 1,688 B | 377.63 ns / 1,104 B | 741.37 ns / 1,808 B |
| Stream, 1 of 16 items, 5 stages, async | 1.985 µs / 1,256 B | 2.477 µs / 1,752 B | 3.764 µs / 4,720 B | 2.655 µs / 2,336 B | 2.426 µs / 1,752 B | 2.979 µs / 2,536 B |
| Short-circuit at first of 3 stages, warm | 4.88 ns / 0 B | 5.72 ns / 0 B | 111.61 ns / 704 B | 72.43 ns / 496 B | 36.11 ns / 0 B | 72.04 ns / 384 B |
| Async hooks, successful first call | 1.050 µs / 448 B | 1.518 µs / 504 B | 1.570 µs / 1,352 B | 1.341 µs / 808 B | 1.296 µs / 560 B | 2.182 µs / 856 B |
| Async hooks, successful warm call | 37.28 ns / 0 B | 60.46 ns / 0 B | 626.77 ns / 944 B | 582.95 ns / 2,909 B | 82.83 ns / 0 B | 600.53 ns / 480 B |
| Async hooks, outer hook fails on first call | 5.755 µs / 1,744 B | 10.522 µs / 3,808 B | 10.140 µs / 3,384 B | 9.811 µs / 3,216 B | 7.679 µs / 2,968 B | 12.730 µs / 4,872 B |

### Reverse order

| Workload | Zendiator | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 1.65 ns / 0 B | 9.22 ns / 0 B | 44.16 ns / 200 B | 45.71 ns / 64 B | 32.68 ns / 0 B | 36.79 ns / 0 B |
| Send, 5 stages | 7.01 ns / 0 B | 19.67 ns / 0 B | 163.85 ns / 1,016 B | 105.94 ns / 544 B | 42.02 ns / 0 B | 183.77 ns / 0 B |
| Resolve again in the same scope and send, 0 stages | 30.94 ns / 0 B | 45.13 ns / 0 B | 78.22 ns / 200 B | 80.41 ns / 64 B | 61.66 ns / 0 B | 77.75 ns / 0 B |
| Resolve again in the same scope and send, 5 stages | 46.74 ns / 0 B | 55.50 ns / 0 B | 195.29 ns / 1,016 B | 145.44 ns / 544 B | 77.08 ns / 0 B | 222.15 ns / 0 B |
| Create a scope, send once and dispose, 5 stages | 121.22 ns / 560 B | 149.34 ns / 568 B | 252.29 ns / 1,400 B | 211.64 ns / 920 B | 531.21 ns / 1,712 B | 379.11 ns / 856 B |
| Notify 16 synchronous subscribers | 145.13 ns / 0 B | N/A | 1,165.25 ns / 4,784 B | 228.70 ns / 0 B | 209.78 ns / 0 B | 1,004.07 ns / 0 B |
| Stream, all 1,024 items, 5 stages, sync | 14.783 µs / 928 B | 106.837 µs / 1,104 B | 211.958 µs / 3,592 B | 109.380 µs / 1,689 B | 105.834 µs / 1,105 B | 126.890 µs / 1,808 B |
| Stream, all 1,024 items, 5 stages, async | 164.840 µs / 1,586 B | 314.962 µs / 1,755 B | 603.728 µs / 4,726 B | 311.326 µs / 2,339 B | 311.871 µs / 1,756 B | 353.159 µs / 2,539 B |
| Stream, 1 of 16 items, 5 stages, sync | 306.55 ns / 928 B | 346.99 ns / 1,104 B | 1,002.63 ns / 3,592 B | 448.07 ns / 1,688 B | 377.74 ns / 1,104 B | 747.24 ns / 1,808 B |
| Stream, 1 of 16 items, 5 stages, async | 2.224 µs / 1,256 B | 2.395 µs / 1,752 B | 5.922 µs / 4,720 B | 2.814 µs / 2,336 B | 2.427 µs / 1,752 B | 3.066 µs / 2,536 B |
| Short-circuit at first of 3 stages, warm | 6.39 ns / 0 B | 7.23 ns / 0 B | 121.07 ns / 704 B | 78.65 ns / 496 B | 46.14 ns / 0 B | 82.39 ns / 384 B |
| Async hooks, successful first call | 0.995 µs / 448 B | 1.905 µs / 504 B | 1.550 µs / 1,352 B | 1.357 µs / 808 B | 1.379 µs / 560 B | 2.126 µs / 856 B |
| Async hooks, successful warm call | 45.68 ns / 0 B | 62.46 ns / 0 B | 934.94 ns / 944 B | 642.18 ns / 2,892 B | 81.59 ns / 0 B | 599.94 ns / 480 B |
| Async hooks, outer hook fails on first call | 5.229 µs / 1,680 B | 11.194 µs / 3,808 B | 10.144 µs / 3,448 B | 8.254 µs / 3,216 B | 8.459 µs / 2,968 B | 13.546 µs / 4,872 B |

The five-stage Send uses five synchronous Before hooks in Zendiator. Early stream exit includes reading one item and disposing the enumerator. Zendiator’s stream stages forward the input cursor. Each competing stage enumerates the upstream sequence and yields its items. The Zendiator input cursor in this run opts into `[GeneratePooledProducer]` for its asynchronous move helper, a feature available since 1.0.0.

Zendiator’s upper 99.9% confidence bound for mean time fell below every peer’s lower bound in 14 workloads in the forward run and 13 in reverse. For asynchronous early exit, the reverse run’s interval overlapped a competitor’s. [Measurement conditions](benchmarks/README.md) · [All 166 measurements (CSV)](benchmarks/results/f2973af-representative-summary.csv)

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
