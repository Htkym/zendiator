using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.Benchmarks;

namespace Zendiator.Benchmark.Tests;

public sealed class CacheContractTests
{
    [Fact]
    public async Task ZeroBehavior_Scoped_reuses_within_scope_and_isolates_across_scopes()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        object? firstScopeHandler;
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.Zendiator>();
            Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
            Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
            Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
            var h1 = scope.ServiceProvider.GetRequiredService<Zp0Handler>();
            var h2 = scope.ServiceProvider.GetRequiredService<Zp0Handler>();
            Assert.Same(h1, h2);
            firstScopeHandler = h1;
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.Zendiator>();
            Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
            Assert.NotSame(firstScopeHandler, scope.ServiceProvider.GetRequiredService<Zp0Handler>());
        }
    }

    [Fact]
    public async Task ZeroBehavior_Transient_resolves_fresh_per_send()
    {
        var services = new ServiceCollection();
        var calls = 0;
        services.AddTransient<Zp0Handler>(_ =>
        {
            Interlocked.Increment(ref calls);
            return new Zp0Handler();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.Zendiator>();
        Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
        Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
        Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
        Assert.Equal(3, calls);
        var h1 = scope.ServiceProvider.GetRequiredService<Zp0Handler>();
        var h2 = scope.ServiceProvider.GetRequiredService<Zp0Handler>();
        Assert.NotSame(h1, h2);
    }

    [Fact]
    public async Task ZeroBehavior_Singleton_shares_across_scopes()
    {
        var services = new ServiceCollection();
        services.AddZendiator(ServiceLifetime.Singleton);
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.Zendiator>();
            Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
            Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.Zendiator>();
            Assert.Equal(42, await mediator.SendAsync(new Zp0(41)));
            Assert.Same(provider.GetRequiredService<global::Zendiator.Benchmarks.Zendiator>(), mediator);
        }
    }

    [Fact]
    public async Task Pipeline_Scoped_counts_match_depth_warm()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.IZendiator>();
        for (var i = 0; i < 3; i++)
            Assert.Equal(42, await mediator.SendAsync(new Zp5(41)));
        ZrCounters.Reset();
        Assert.Equal(42, await mediator.SendAsync(new Zp5(41)));
        Assert.Equal((1, 1, 1, 1, 1), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));
        ZrCounters.Reset();
        Assert.Equal(42, await mediator.SendAsync(new Zp1(41)));
        Assert.Equal((1, 0, 0, 0, 0), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));
    }

    [Fact]
    public async Task E02_direct_chain_returns_expected_values()
    {
        var benchmarks = new PipelineBenchmarks();
        benchmarks.Setup();
        try
        {
            Assert.Equal(42, await benchmarks.E02Direct0());
            Assert.Equal(42, await benchmarks.E02Direct1());
            Assert.Equal(42, await benchmarks.E02Direct3());
            Assert.Equal(42, await benchmarks.E02Direct5());
        }
        finally
        {
            benchmarks.Cleanup();
        }
    }
}
