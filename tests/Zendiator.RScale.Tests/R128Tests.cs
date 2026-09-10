using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.RScale.Tests;

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
