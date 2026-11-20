# Zendiator

[日本語](README.ja.md)

Zendiator is a fast, source-generated mediator for .NET 10. **Sending a request through five synchronous Before hooks took 6.40–6.71 ns on average and allocated 0 B per call.** The mediator and request were ready before timing began. Define a handler and call `SendAsync`. The source generator connects the request to the handler at build time.

## Performance

The measurements used Windows x64, .NET 10.0.14 and Zendiator revision `0d53c53` from before the 1.0.0 release. Each cell gives the **mean time / bytes allocated per operation**. The two runs measured the same 83 combinations in opposite orders. Immediate.Handlers has no corresponding notification workload, shown as `N/A`.

Zendiator had the lowest mean time on all 14 workloads in both orders. It also had or shared the lowest allocation in 12 workloads in each order.

### Forward order

| Workload | Zendiator | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 1.65 ns / 0 B | 11.82 ns / 0 B | 51.49 ns / 200 B | 64.79 ns / 64 B | 43.08 ns / 0 B | 40.25 ns / 0 B |
| Send, 5 stages | 6.71 ns / 0 B | 20.95 ns / 0 B | 184.12 ns / 1,016 B | 113.78 ns / 544 B | 49.77 ns / 0 B | 188.89 ns / 0 B |
| Resolve again in the same scope and send, 0 stages | 36.69 ns / 0 B | 47.57 ns / 0 B | 79.66 ns / 200 B | 97.67 ns / 64 B | 63.36 ns / 0 B | 80.63 ns / 0 B |
| Resolve again in the same scope and send, 5 stages | 36.98 ns / 0 B | 58.94 ns / 0 B | 197.74 ns / 1,016 B | 154.22 ns / 544 B | 80.28 ns / 0 B | 257.67 ns / 0 B |
| Create a scope, send once and dispose, 5 stages | 126.38 ns / 624 B | 173.62 ns / 568 B | 259.77 ns / 1,400 B | 213.77 ns / 920 B | 519.08 ns / 1,712 B | 400.80 ns / 856 B |
| Notify 16 synchronous subscribers | 140.12 ns / 0 B | N/A | 1,191.93 ns / 4,784 B | 229.14 ns / 0 B | 234.45 ns / 0 B | 898.58 ns / 0 B |
| Stream, all 1,024 items, 5 stages, sync | 15.219 µs / 976 B | 110.913 µs / 1,104 B | 221.922 µs / 3,594 B | 113.622 µs / 1,689 B | 111.175 µs / 1,105 B | 130.435 µs / 1,809 B |
| Stream, all 1,024 items, 5 stages, async | 201.291 µs / 116,322 B | 344.973 µs / 1,755 B | 640.243 µs / 4,726 B | 328.871 µs / 2,339 B | 317.359 µs / 1,756 B | 376.680 µs / 2,540 B |
| Stream, 1 of 16 items, 5 stages, sync | 317.31 ns / 976 B | 385.47 ns / 1,104 B | 1,021.16 ns / 3,592 B | 479.10 ns / 1,688 B | 396.62 ns / 1,104 B | 777.55 ns / 1,808 B |
| Stream, 1 of 16 items, 5 stages, async | 2.260 µs / 1,416 B | 2.508 µs / 1,752 B | 3.926 µs / 4,720 B | 2.621 µs / 2,336 B | 2.609 µs / 1,752 B | 3.265 µs / 2,536 B |
| Short-circuit at first of 3 stages, warm | 4.83 ns / 0 B | 6.01 ns / 0 B | 108.08 ns / 704 B | 72.31 ns / 496 B | 36.04 ns / 0 B | 75.60 ns / 384 B |
| Async hooks, successful first call | 1.023 µs / 448 B | 1.744 µs / 504 B | 1.629 µs / 1,352 B | 1.393 µs / 808 B | 1.335 µs / 560 B | 2.192 µs / 856 B |
| Async hooks, successful warm call | 37.93 ns / 0 B | 58.05 ns / 0 B | 701.63 ns / 944 B | 553.04 ns / 496 B | 77.71 ns / 0 B | 563.80 ns / 480 B |
| Async hooks, outer hook fails on first call | 4.422 µs / 1,680 B | 10.076 µs / 3,808 B | 10.259 µs / 3,384 B | 7.776 µs / 3,216 B | 7.779 µs / 2,968 B | 11.838 µs / 4,872 B |

### Reverse order

| Workload | Zendiator | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send, 0 stages | 1.74 ns / 0 B | 11.41 ns / 0 B | 45.66 ns / 200 B | 46.86 ns / 64 B | 37.79 ns / 0 B | 35.72 ns / 0 B |
| Send, 5 stages | 6.40 ns / 0 B | 20.25 ns / 0 B | 166.35 ns / 1,016 B | 104.04 ns / 544 B | 42.12 ns / 0 B | 184.23 ns / 0 B |
| Resolve again in the same scope and send, 0 stages | 35.25 ns / 0 B | 44.22 ns / 0 B | 77.91 ns / 200 B | 85.60 ns / 64 B | 62.72 ns / 0 B | 73.56 ns / 0 B |
| Resolve again in the same scope and send, 5 stages | 35.57 ns / 0 B | 59.94 ns / 0 B | 195.30 ns / 1,016 B | 148.86 ns / 544 B | 73.49 ns / 0 B | 221.97 ns / 0 B |
| Create a scope, send once and dispose, 5 stages | 135.44 ns / 624 B | 159.43 ns / 568 B | 266.79 ns / 1,400 B | 208.16 ns / 920 B | 555.78 ns / 1,712 B | 383.04 ns / 856 B |
| Notify 16 synchronous subscribers | 145.94 ns / 0 B | N/A | 1,221.92 ns / 4,784 B | 239.62 ns / 0 B | 204.91 ns / 0 B | 871.66 ns / 0 B |
| Stream, all 1,024 items, 5 stages, sync | 15.125 µs / 976 B | 110.807 µs / 1,104 B | 222.092 µs / 3,592 B | 110.216 µs / 1,688 B | 117.380 µs / 1,104 B | 128.873 µs / 1,808 B |
| Stream, all 1,024 items, 5 stages, async | 192.900 µs / 116,320 B | 325.066 µs / 1,752 B | 628.665 µs / 4,720 B | 324.403 µs / 2,336 B | 323.658 µs / 1,752 B | 366.102 µs / 2,536 B |
| Stream, 1 of 16 items, 5 stages, sync | 296.50 ns / 976 B | 385.53 ns / 1,104 B | 904.29 ns / 3,592 B | 525.73 ns / 1,688 B | 379.99 ns / 1,104 B | 797.66 ns / 1,808 B |
| Stream, 1 of 16 items, 5 stages, async | 2.048 µs / 1,416 B | 2.283 µs / 1,752 B | 3.612 µs / 4,720 B | 2.638 µs / 2,336 B | 2.314 µs / 1,752 B | 2.884 µs / 2,536 B |
| Short-circuit at first of 3 stages, warm | 4.63 ns / 0 B | 5.44 ns / 0 B | 98.72 ns / 704 B | 69.38 ns / 496 B | 34.87 ns / 0 B | 67.59 ns / 384 B |
| Async hooks, successful first call | 0.995 µs / 448 B | 1.456 µs / 504 B | 1.466 µs / 1,352 B | 1.306 µs / 808 B | 1.231 µs / 560 B | 2.083 µs / 856 B |
| Async hooks, successful warm call | 36.08 ns / 0 B | 57.34 ns / 0 B | 596.42 ns / 944 B | 546.56 ns / 496 B | 82.08 ns / 0 B | 558.79 ns / 2,902 B |
| Async hooks, outer hook fails on first call | 4.578 µs / 1,680 B | 10.301 µs / 3,808 B | 9.910 µs / 3,384 B | 9.699 µs / 3,216 B | 7.846 µs / 2,968 B | 11.936 µs / 4,872 B |

The five-stage Send uses five synchronous Before hooks in Zendiator. Early stream exit includes reading one item and disposing the enumerator. Zendiator’s stream stages forward the input cursor. Each competing stage enumerates the upstream sequence and yields its items.

Zendiator’s upper 99.9% confidence bound for mean time fell below every peer’s lower bound in 13 workloads in the forward run and 13 in reverse. [Measurement conditions](benchmarks/README.md) · [All 166 measurements (CSV)](benchmarks/results/0d53c53-representative-summary.csv)

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
