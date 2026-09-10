using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.RScale.R32;

// R=32 scale study: SameRoute, OneShot, U0, and fixed-order distributions.
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

    [Benchmark(Description = "R32 SameRoute0 K=1")]
    public int SameRoute0K1()
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Q0(0)).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "R32 SameRoute0 K=16")]
    public int SameRoute0K16()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 16; i++)
            sum += mediator.SendAsync(new Q0(0)).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "R32 SameRoute5 K=1")]
    public int SameRoute5K1()
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Q1(1)).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "R32 SameRoute5 K=16")]
    public long SameRoute5K16()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        long sum = 0;
        for (var i = 0; i < 16; i++)
            sum += mediator.SendAsync(new Q1(1)).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "R32 OneShot")]
    public long OneShot()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        return Sweep.OneShotAsync(mediator).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "R32 U0")]
    public int U0()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        return mediator.GetHashCode();
    }

    // Fixed-order RoundRobin (Q1..Q16) and MixedHotCold (hot Q1 sets).
    [Benchmark(Description = "R32 RoundRobin5")]
    public long RoundRobin5()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        long sum = 0;
        sum += mediator.SendAsync(new Q1(1)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q2(2)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q3(3)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q4(4)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q5(5)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q6(6)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q7(7)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q8(8)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q9(9)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q10(10)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q11(11)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q12(12)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q13(13)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q14(14)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q15(15)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q16(16)).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "R32 MixedHotCold5")]
    public long MixedHotCold5()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        long sum = 0;
        for (var set = 0; set < 3; set++)
            sum += mediator.SendAsync(new Q1(1)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q2(2)).GetAwaiter().GetResult();
        for (var set = 0; set < 3; set++)
            sum += mediator.SendAsync(new Q1(1)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q3(3)).GetAwaiter().GetResult();
        for (var set = 0; set < 3; set++)
            sum += mediator.SendAsync(new Q1(1)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q4(4)).GetAwaiter().GetResult();
        for (var set = 0; set < 3; set++)
            sum += mediator.SendAsync(new Q1(1)).GetAwaiter().GetResult();
        sum += mediator.SendAsync(new Q5(5)).GetAwaiter().GetResult();
        return sum;
    }
}
