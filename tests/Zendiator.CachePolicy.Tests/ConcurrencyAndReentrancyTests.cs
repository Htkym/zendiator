using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class ConcurrencyAndReentrancyTests
{
    [Fact]
    public async Task Concurrent_first_use_shares_single_scoped_instance()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddScoped<Val0Handler>(_ =>
        {
            Interlocked.Increment(ref Val0Handler.FactoryCalls);
            return new Val0Handler();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).SelectMany(task => Enumerable.Range(0, 3).Select(i =>
            Task.Run(async () => await mediator.SendAsync(new Val0(task * 10 + i))))));
        for (var task = 0; task < 8; task++)
            for (var i = 0; i < 3; i++)
                Assert.Equal(task * 10 + i + 1, results[task * 3 + i]);
        Assert.Equal(1, Val0Handler.FactoryCalls);
    }

    [Fact]
    public async Task Concurrent_transient_use_constructs_per_send()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddTransient<Val0Handler>(_ =>
        {
            Interlocked.Increment(ref Val0Handler.FactoryCalls);
            return new Val0Handler();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Task.Run(async () => await mediator.SendAsync(new Val0(i)))));
        for (var i = 0; i < 8; i++)
            Assert.Equal(i + 1, results[i]);
        Assert.Equal(8, Val0Handler.FactoryCalls);
    }

    [Fact]
    public async Task Concurrent_distinct_routes_stay_independent()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var val0 = Task.Run(async () => await mediator.SendAsync(new Val0(41)));
        var pipe = Task.Run(async () => await mediator.SendAsync(new PipeReq(1)));
        var gate = Task.Run(async () => await mediator.SendAsync(new GateReq(true)));
        var tok = Task.Run(async () => await mediator.SendAsync(new TokReq(CancellationToken.None)));
        Assert.Equal(42, await val0);
        Assert.Equal(1001, await pipe);
        Assert.Equal(7, await gate);
        Assert.Equal(CancellationToken.None, await tok);
    }

    [Fact]
    public async Task Factory_reentrancy_into_another_route_completes()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        var entered = 0;
        services.AddScoped<Val0Handler>(sp =>
        {
            Interlocked.Increment(ref Val0Handler.FactoryCalls);
            if (Interlocked.CompareExchange(ref entered, 1, 0) == 0)
            {
                var inner = sp.GetRequiredService<IZendiator>().SendAsync(new PipeReq(1)).GetAwaiter().GetResult();
                Assert.Equal(1001, inner);
            }
            return new Val0Handler();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
        Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
    }

    [Fact]
    public async Task Behavior_reentrant_sends_stay_independent()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        for (var i = 0; i < 3; i++)
            Assert.Equal(1007, await mediator.SendAsync(new ReReq(5)));
        Assert.Equal(3, ReenterBehavior.HandleCalls);
    }
}
