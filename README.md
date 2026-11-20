# Zendiator

[日本語](README.ja.md)

Zendiator is an in-process mediator for .NET 10 and C# 14. Its source generator connects requests to handlers at build time and generates registration code for `Microsoft.Extensions.DependencyInjection`. Use `SendAsync` to send a request, `PublishAsync` to notify subscribers and `StreamAsync` to read an asynchronous stream.

**Sending a request through five synchronous Before hooks took 7.01–9.58 ns on average and allocated 0 B per call.** The mediator and request were ready before timing began.

[Send your first request](#send-your-first-request) · [Using Zendiator as a MediatR alternative](docs/migrating-from-mediatr.md) · [Native AOT configurations verified on Windows x64](docs/release/known-limitations.md#native-aot-and-dispatch-boundaries)

## Performance

We compared Zendiator 1.0.1, Immediate.Handlers 4.3.0, MediatR 15.0.0, Mediator 3.0.2, DispatchR.Mediator 2.3.1 and Foundatio.Mediator 1.3.3 on Windows x64 with .NET 10.0.14. Immediate.Handlers has no matching notification workload, shown as `N/A`. We measured the remaining 83 library and workload combinations in forward and reverse order.

Zendiator had the lowest mean time in all 14 workloads in both orders. It allocated fewer bytes than every competitor in seven workloads and tied at 0 B in the other seven.

[Time and allocation for all six libraries by workload](benchmarks/README.md#results-by-workload)

### Mean time in forward order

| Workload | Zendiator 1.0.1 | Immediate 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR 2.3.1 | Foundatio 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 2.04 ns | 13.19 ns | 70.45 ns | 70.59 ns | 45.65 ns | 44.90 ns |
| Send, 5 stages | 9.58 ns | 26.84 ns | 239.07 ns | 153.75 ns | 52.36 ns | 235.80 ns |
| Resolve again in the same scope and send, 0 stages | 33.52 ns | 54.41 ns | 82.66 ns | 114.87 ns | 79.80 ns | 86.41 ns |
| Resolve again in the same scope and send, 5 stages | 35.75 ns | 57.85 ns | 197.13 ns | 147.42 ns | 83.03 ns | 226.87 ns |
| Create a scope, send once and dispose, 5 stages | 131.93 ns | 155.14 ns | 268.97 ns | 227.14 ns | 774.62 ns | 389.28 ns |
| Notify 16 synchronous subscribers | 143.94 ns | N/A | 1,249.78 ns | 230.08 ns | 209.27 ns | 1,006.80 ns |
| Stream, all 1,024 items, 5 stages, sync | 15.242 µs | 108.246 µs | 216.464 µs | 108.292 µs | 106.468 µs | 127.324 µs |
| Stream, all 1,024 items, 5 stages, async | 167.413 µs | 311.844 µs | 624.129 µs | 329.834 µs | 316.544 µs | 352.939 µs |
| Stream, 1 of 16 items, 5 stages, sync | 306.07 ns | 348.98 ns | 951.59 ns | 453.85 ns | 377.63 ns | 741.37 ns |
| Stream, 1 of 16 items, 5 stages, async | 1.985 µs | 2.477 µs | 3.764 µs | 2.655 µs | 2.426 µs | 2.979 µs |
| Short-circuit at first of 3 stages, warm | 4.88 ns | 5.72 ns | 111.61 ns | 72.43 ns | 36.11 ns | 72.04 ns |
| Async hooks, successful first call | 1.050 µs | 1.518 µs | 1.570 µs | 1.341 µs | 1.296 µs | 2.182 µs |
| Async hooks, successful warm call | 37.28 ns | 60.46 ns | 626.77 ns | 582.95 ns | 82.83 ns | 600.53 ns |
| Async hooks, outer hook fails on first call | 5.755 µs | 10.522 µs | 10.140 µs | 9.811 µs | 7.679 µs | 12.730 µs |

### Mean time in reverse order

| Workload | Zendiator 1.0.1 | Immediate 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR 2.3.1 | Foundatio 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 1.65 ns | 9.22 ns | 44.16 ns | 45.71 ns | 32.68 ns | 36.79 ns |
| Send, 5 stages | 7.01 ns | 19.67 ns | 163.85 ns | 105.94 ns | 42.02 ns | 183.77 ns |
| Resolve again in the same scope and send, 0 stages | 30.94 ns | 45.13 ns | 78.22 ns | 80.41 ns | 61.66 ns | 77.75 ns |
| Resolve again in the same scope and send, 5 stages | 46.74 ns | 55.50 ns | 195.29 ns | 145.44 ns | 77.08 ns | 222.15 ns |
| Create a scope, send once and dispose, 5 stages | 121.22 ns | 149.34 ns | 252.29 ns | 211.64 ns | 531.21 ns | 379.11 ns |
| Notify 16 synchronous subscribers | 145.13 ns | N/A | 1,165.25 ns | 228.70 ns | 209.78 ns | 1,004.07 ns |
| Stream, all 1,024 items, 5 stages, sync | 14.783 µs | 106.837 µs | 211.958 µs | 109.380 µs | 105.834 µs | 126.890 µs |
| Stream, all 1,024 items, 5 stages, async | 164.840 µs | 314.962 µs | 603.728 µs | 311.326 µs | 311.871 µs | 353.159 µs |
| Stream, 1 of 16 items, 5 stages, sync | 306.55 ns | 346.99 ns | 1,002.63 ns | 448.07 ns | 377.74 ns | 747.24 ns |
| Stream, 1 of 16 items, 5 stages, async | 2.224 µs | 2.395 µs | 5.922 µs | 2.814 µs | 2.427 µs | 3.066 µs |
| Short-circuit at first of 3 stages, warm | 6.39 ns | 7.23 ns | 121.07 ns | 78.65 ns | 46.14 ns | 82.39 ns |
| Async hooks, successful first call | 0.995 µs | 1.905 µs | 1.550 µs | 1.357 µs | 1.379 µs | 2.126 µs |
| Async hooks, successful warm call | 45.68 ns | 62.46 ns | 934.94 ns | 642.18 ns | 81.59 ns | 599.94 ns |
| Async hooks, outer hook fails on first call | 5.229 µs | 11.194 µs | 10.144 µs | 8.254 µs | 8.459 µs | 13.546 µs |

### Bytes allocated per operation

A single value applies to both orders. Where allocations differed, the cell shows forward / reverse.

| Workload | Zendiator 1.0.1 | Immediate 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR 2.3.1 | Foundatio 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 0 | 0 | 200 | 64 | 0 | 0 |
| Send, 5 stages | 0 | 0 | 1,016 | 544 | 0 | 0 |
| Resolve again in the same scope and send, 0 stages | 0 | 0 | 200 | 64 | 0 | 0 |
| Resolve again in the same scope and send, 5 stages | 0 | 0 | 1,016 | 544 | 0 | 0 |
| Create a scope, send once and dispose, 5 stages | 560 | 568 | 1,400 | 920 | 1,712 | 856 |
| Notify 16 synchronous subscribers | 0 | N/A | 4,784 | 0 | 0 | 0 |
| Stream, all 1,024 items, 5 stages, sync | 928 | 1,104 | 3,593 / 3,592 | 1,688 / 1,689 | 1,104 / 1,105 | 1,808 |
| Stream, all 1,024 items, 5 stages, async | 1,586 | 1,755 | 4,726 | 2,340 / 2,339 | 1,756 | 2,539 |
| Stream, 1 of 16 items, 5 stages, sync | 928 | 1,104 | 3,592 | 1,688 | 1,104 | 1,808 |
| Stream, 1 of 16 items, 5 stages, async | 1,256 | 1,752 | 4,720 | 2,336 | 1,752 | 2,536 |
| Short-circuit at first of 3 stages, warm | 0 | 0 | 704 | 496 | 0 | 384 |
| Async hooks, successful first call | 448 | 504 | 1,352 | 808 | 560 | 856 |
| Async hooks, successful warm call | 0 | 0 | 944 | 2,909 / 2,892 | 0 | 480 |
| Async hooks, outer hook fails on first call | 1,744 / 1,680 | 3,808 | 3,384 / 3,448 | 3,216 | 2,968 | 4,872 |

The five-stage Send uses five synchronous Before hooks in Zendiator. Early stream exit includes reading one item and disposing the enumerator. Zendiator’s stream stages forward the input cursor. Each competing stage enumerates the upstream sequence and yields its items. The Zendiator input cursor in this run opts into `[GeneratePooledProducer]` for its asynchronous move helper, a feature available since 1.0.0.

Zendiator’s upper 99.9% confidence bound for mean time fell below every peer’s lower bound in 14 workloads in the forward run and 13 in reverse. For asynchronous early exit, the reverse run’s interval overlapped a competitor’s. [Measurement conditions](benchmarks/README.md) · [All 166 measurements (CSV)](benchmarks/results/f2973af-representative-summary.csv)

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
