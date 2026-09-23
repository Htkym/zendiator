using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class DisposalContractTests
{
    [Fact]
    public async Task Async_dispose_owns_single_handler_dispose()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.False(mediator is IDisposable);
        Assert.Equal(5, await mediator.SendAsync(new DispReq()));
        Assert.Equal(5, await mediator.SendAsync(new DispReq()));
        await scope.DisposeAsync();
        Assert.Equal(1, DispHandler.DisposeCount);
        await provider.DisposeAsync();
        Assert.Equal(1, DispHandler.DisposeCount);
    }

    [Fact]
    public async Task Two_providers_stay_isolated()
    {
        TestCounters.ResetAll();
        var servicesA = new ServiceCollection();
        servicesA.AddZendiator();
        var servicesB = new ServiceCollection();
        servicesB.AddTransient<Guid0Handler>(_ =>
        {
            Interlocked.Increment(ref Guid0Handler.FactoryCalls);
            return new Guid0Handler();
        });
        servicesB.AddZendiator();
        await using var providerA = servicesA.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var providerB = servicesB.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scopeA = providerA.CreateAsyncScope();
        await using var scopeB = providerB.CreateAsyncScope();
        var mediatorA = scopeA.ServiceProvider.GetRequiredService<IZendiator>();
        var mediatorB = scopeB.ServiceProvider.GetRequiredService<IZendiator>();
        var a1 = await mediatorA.SendAsync(new Guid0());
        var b1 = await mediatorB.SendAsync(new Guid0());
        var a2 = await mediatorA.SendAsync(new Guid0());
        var b2 = await mediatorB.SendAsync(new Guid0());
        Assert.Equal(a1, a2);
        Assert.Equal(b1, b2);
        Assert.NotEqual(a1, b1);
    }
}
