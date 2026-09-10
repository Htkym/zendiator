using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class DisposalContractTests
{
    [Fact]
    public async Task Disposed_scope_rejects_zero_stage_send_in_every_state()
    {
        foreach (var warmSends in new[] { 0, 1, 3 })
        {
            TestCounters.ResetAll();
            var services = new ServiceCollection();
            services.AddZendiator();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            for (var i = 0; i < warmSends; i++)
                Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
            await scope.DisposeAsync();
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await mediator.SendAsync(new Val0(41)));
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disposed_scope_rejects_pipeline_send_in_every_state()
    {
        foreach (var warmSends in new[] { 0, 1, 3 })
        {
            TestCounters.ResetAll();
            var services = new ServiceCollection();
            services.AddZendiator();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            for (var i = 0; i < warmSends; i++)
                Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
            await scope.DisposeAsync();
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await mediator.SendAsync(new PipeReq(1)));
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disposed_root_rejects_singleton_send()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator(ServiceLifetime.Singleton);
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IZendiator>();
        Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
        Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        await provider.DisposeAsync();
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await mediator.SendAsync(new Val0(41)));
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await mediator.SendAsync(new PipeReq(1)));
    }

    [Fact]
    public async Task Async_dispose_owns_single_dispose_and_rejects_reuse()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(5, await mediator.SendAsync(new DispReq()));
        Assert.Equal(5, await mediator.SendAsync(new DispReq()));
        await scope.DisposeAsync();
        Assert.Equal(1, DispHandler.DisposeCount);
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await mediator.SendAsync(new DispReq()));
        await provider.DisposeAsync();
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
        Assert.NotEqual(b1, b2);
        Assert.NotEqual(a1, b1);
    }
}
