using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

/// <summary>Cold start: registration, provider build and first dispatch.</summary>
[MemoryDiagnoser]
public class StartupBenchmarks
{
    [Benchmark(Description = "Zr cold start")]
    public int ZrColdStart()
    {
        using var provider = ZrHost.CreateScoped();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Zp0(41)).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "MediatR cold start")]
    public int MediatRColdStart()
    {
        using var provider = MrHost.CreateProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>().Send(new MrP0(41)).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Mo cold start")]
    public int MoColdStart()
    {
        using var provider = MoHost.CreateProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>().Send(new MoP0(41)).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "DSoft cold start")]
    public int DsColdStart()
    {
        using var provider = DsHost.CreateProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>().Send(new DsP0(41)).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "DispatchR cold start")]
    public int DrColdStart()
    {
        using var provider = DrHost.CreateProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<global::DispatchR.IMediator>().Send<DrP0, ValueTask<int>>(new DrP0(41), CancellationToken.None).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Foundatio cold start")]
    public int FoColdStart()
    {
        using var provider = FoHost.CreateProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<global::Foundatio.Mediator.IMediator>().Invoke<int>(new FoPing(41));
    }
}
