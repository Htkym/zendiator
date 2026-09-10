using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zendiator.PipeDepth.Tests;

public sealed class DepthCorrectnessTests
{
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        PipeDepth.ZendiatorServiceCollectionExtensions.AddZendiator(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task Depths_return_value_plus_one()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<PipeDepth.IZendiator>();
        Assert.Equal(42, await mediator.SendAsync(new PipeDepth.P2(41)));
        Assert.Equal(42, await mediator.SendAsync(new PipeDepth.P3(41)));
        Assert.Equal(42, await mediator.SendAsync(new PipeDepth.P4(41)));
        Assert.Equal(42, await mediator.SendAsync(new PipeDepth.P5(41)));
        Assert.Equal(42, await mediator.SendAsync(new PipeDepth.P6(41)));
        Assert.Equal(42, await mediator.SendAsync(new PipeDepth.P8(41)));
    }

    [Fact]
    public async Task Observable_routes_transform_in_nesting_order()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<PipeDepth.IZendiator>();
        Assert.Equal(41, await mediator.SendAsync(new PipeDepth.O0(41)));
        Assert.Equal(151, await mediator.SendAsync(new PipeDepth.O1(41)));
        Assert.Equal(371, await mediator.SendAsync(new PipeDepth.O3(41)));
        Assert.Equal(591, await mediator.SendAsync(new PipeDepth.O5(41)));
        Assert.Equal(115, await mediator.SendAsync(new PipeDepth.O1(5)));
        Assert.Equal(330, await mediator.SendAsync(new PipeDepth.O3(0)));
    }

    [Fact]
    public async Task Post_only_observable_routes_add_100_per_layer()
    {
        await using var provider = CreateProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<PipeDepth.IZendiator>();
        Assert.Equal(41, await mediator.SendAsync(new PipeDepth.Oz0(41)));
        Assert.Equal(141, await mediator.SendAsync(new PipeDepth.Oz1(41)));
        Assert.Equal(341, await mediator.SendAsync(new PipeDepth.Oz3(41)));
        Assert.Equal(541, await mediator.SendAsync(new PipeDepth.Oz5(41)));
        Assert.Equal(105, await mediator.SendAsync(new PipeDepth.Oz1(5)));
        Assert.Equal(300, await mediator.SendAsync(new PipeDepth.Oz3(0)));
    }
}
