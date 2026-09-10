using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zendiator.RScale.Tests;

public sealed class R1Tests
{
    [Fact]
    public async Task Routes_return_expected_values()
    {
        var services = new ServiceCollection();
        R1.ZendiatorServiceCollectionExtensions.AddZendiator(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<R1.IZendiator>();
        Assert.Equal(1, await mediator.SendAsync(new R1.Q0(0)));
        Assert.Equal(43, await mediator.SendAsync(new R1.Q0(42)));
    }
}

public sealed class R32Tests
{
    [Fact]
    public async Task Full_sweep_sums_all_routes()
    {
        var services = new ServiceCollection();
        R32.ZendiatorServiceCollectionExtensions.AddZendiator(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<R32.IZendiator>();
        Assert.Equal(1, await mediator.SendAsync(new R32.Q0(0)));
        Assert.Equal(2, await mediator.SendAsync(new R32.Q1(1)));
        Assert.Equal(R32.RScaleExpected.FullSweepSum, await R32.Sweep.OneShotAsync(mediator));
    }

    [Fact]
    public async Task Distribution_sums_match_fixed_order()
    {
        var services = new ServiceCollection();
        R32.ZendiatorServiceCollectionExtensions.AddZendiator(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<R32.IZendiator>();
        long roundRobin = 0;
        roundRobin += await mediator.SendAsync(new R32.Q1(1));
        roundRobin += await mediator.SendAsync(new R32.Q2(2));
        roundRobin += await mediator.SendAsync(new R32.Q3(3));
        roundRobin += await mediator.SendAsync(new R32.Q4(4));
        roundRobin += await mediator.SendAsync(new R32.Q5(5));
        roundRobin += await mediator.SendAsync(new R32.Q6(6));
        roundRobin += await mediator.SendAsync(new R32.Q7(7));
        roundRobin += await mediator.SendAsync(new R32.Q8(8));
        roundRobin += await mediator.SendAsync(new R32.Q9(9));
        roundRobin += await mediator.SendAsync(new R32.Q10(10));
        roundRobin += await mediator.SendAsync(new R32.Q11(11));
        roundRobin += await mediator.SendAsync(new R32.Q12(12));
        roundRobin += await mediator.SendAsync(new R32.Q13(13));
        roundRobin += await mediator.SendAsync(new R32.Q14(14));
        roundRobin += await mediator.SendAsync(new R32.Q15(15));
        roundRobin += await mediator.SendAsync(new R32.Q16(16));
        Assert.Equal(152, roundRobin);

        await using var scope2 = provider.CreateAsyncScope();
        var mediator2 = scope2.ServiceProvider.GetRequiredService<R32.IZendiator>();
        long mixed = 0;
        for (var set = 0; set < 3; set++)
            mixed += await mediator2.SendAsync(new R32.Q1(1));
        mixed += await mediator2.SendAsync(new R32.Q2(2));
        for (var set = 0; set < 3; set++)
            mixed += await mediator2.SendAsync(new R32.Q1(1));
        mixed += await mediator2.SendAsync(new R32.Q3(3));
        for (var set = 0; set < 3; set++)
            mixed += await mediator2.SendAsync(new R32.Q1(1));
        mixed += await mediator2.SendAsync(new R32.Q4(4));
        for (var set = 0; set < 3; set++)
            mixed += await mediator2.SendAsync(new R32.Q1(1));
        mixed += await mediator2.SendAsync(new R32.Q5(5));
        Assert.Equal(42, mixed);
    }
}

public sealed class R128Tests
{
    [Fact]
    public async Task Full_sweep_sums_all_routes()
    {
        var services = new ServiceCollection();
        R128.ZendiatorServiceCollectionExtensions.AddZendiator(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<R128.IZendiator>();
        Assert.Equal(1, await mediator.SendAsync(new R128.Q0(0)));
        Assert.Equal(2, await mediator.SendAsync(new R128.Q1(1)));
        Assert.Equal(128, await mediator.SendAsync(new R128.Q127(127)));
        Assert.Equal(R128.RScaleExpected.FullSweepSum, await R128.Sweep.OneShotAsync(mediator));
    }
}
