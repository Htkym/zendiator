using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.RScale.R128;

// R=128 scale study: SameRoute, OneShot, and U0.
[MemoryDiagnoser]
public class ScaleBenchmarks
{
    private ServiceProvider _provider = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        _ = mediator.SendAsync(new Q0(0)).GetAwaiter().GetResult();
        _ = mediator.SendAsync(new Q1(1)).GetAwaiter().GetResult();
        _ = Sweep.OneShotAsync(mediator).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    [Benchmark(Description = "R128 SameRoute0 K=1")]
    public int SameRoute0K1()
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Q0(0)).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "R128 SameRoute0 K=16")]
    public int SameRoute0K16()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 16; i++)
            sum += mediator.SendAsync(new Q0(0)).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "R128 SameRoute5 K=1")]
    public int SameRoute5K1()
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Q1(1)).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "R128 SameRoute5 K=16")]
    public long SameRoute5K16()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        long sum = 0;
        for (var i = 0; i < 16; i++)
            sum += mediator.SendAsync(new Q1(1)).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "R128 OneShot")]
    public long OneShot()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        return Sweep.OneShotAsync(mediator).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "R128 U0")]
    public int U0()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        return mediator.GetHashCode();
    }
}
