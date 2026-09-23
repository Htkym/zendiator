using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class FactoryAndLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Providers_use_their_own_descriptors_after_collection_changes(bool warmFirst, bool restoreDescriptors)
    {
        var services = BaseServices();
        services.AddZendiator();
        using var first = services.BuildServiceProvider();
        using var firstScope = first.CreateScope();
        var firstMediator = firstScope.ServiceProvider.GetRequiredService<IZendiator>();
        Guid? original = warmFirst ? await firstMediator.SendAsync(new Guid0()) : null;

        services.AddTransient<Guid0Handler>();
        using var second = services.BuildServiceProvider();
        using var secondScope = second.CreateScope();
        if (restoreDescriptors) services.RemoveAt(services.Count - 1);
        var secondMediator = secondScope.ServiceProvider.GetRequiredService<IZendiator>();
        var secondA = await secondMediator.SendAsync(new Guid0());
        var secondB = await secondMediator.SendAsync(new Guid0());
        Assert.Equal(secondA, secondB);
        Assert.NotSame(secondScope.ServiceProvider.GetRequiredService<Guid0Handler>(), secondScope.ServiceProvider.GetRequiredService<Guid0Handler>());
        Assert.NotSame(firstScope.ServiceProvider.GetRequiredService<Guid0Handler>(), firstScope.ServiceProvider.GetRequiredService<Guid0Handler>());
        var firstA = await firstMediator.SendAsync(new Guid0());
        Assert.Equal(firstA, await firstMediator.SendAsync(new Guid0()));
        if (original.HasValue) Assert.Equal(original.Value, firstA);
    }

    private static ServiceCollection BaseServices()
    {
        TestCounters.ResetAll();
        return new ServiceCollection();
    }

    private static async Task<ServiceProvider> BuildAsync(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await Task.Yield();
        return provider;
    }

    [Fact]
    public async Task Singleton_mediator_with_scoped_handler_is_rejected()
    {
        var services = BaseServices();
        services.AddScoped<Val0Handler>();
        services.AddZendiator(ServiceLifetime.Singleton);
        await using var provider = await BuildAsync(services);
        var mediator = provider.GetRequiredService<IZendiator>();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await mediator.SendAsync(new Val0(41)));
    }

    [Fact]
    public async Task All_transient_constructs_handler_per_mediator()
    {
        var services = BaseServices();
        services.AddZendiator(ServiceLifetime.Transient);
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var before = ProbeHandler.Constructions;
        Assert.Equal(0, await mediator.SendAsync(new ProbeReq()));
        Assert.Equal(0, await mediator.SendAsync(new ProbeReq()));
        Assert.Equal(0, await mediator.SendAsync(new ProbeReq()));
        Assert.Equal(1, ProbeHandler.Constructions - before);
        var nextMediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.NotSame(mediator, nextMediator);
        Assert.Equal(0, await nextMediator.SendAsync(new ProbeReq()));
        Assert.Equal(2, ProbeHandler.Constructions - before);
    }

    [Fact]
    public async Task Transient_factory_called_once_per_mediator()
    {
        var services = BaseServices();
        services.AddTransient<ProbeHandler>(_ =>
        {
            Interlocked.Increment(ref ProbeHandler.FactoryCalls);
            return new ProbeHandler { Marker = 42 };
        });
        services.AddZendiator();
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        for (var i = 0; i < 3; i++)
            Assert.Equal(42, await mediator.SendAsync(new ProbeReq()));
        Assert.Equal(1, ProbeHandler.FactoryCalls);
    }

    [Fact]
    public async Task Mixed_lifetimes_are_captured_once_per_mediator()
    {
        var services = BaseServices();
        services.AddScoped<PipeB0>(sp =>
        {
            Interlocked.Increment(ref PipeB0.FactoryCalls);
            return new PipeB0();
        });
        services.AddTransient<PipeB1>(sp =>
        {
            Interlocked.Increment(ref PipeB1.FactoryCalls);
            return new PipeB1();
        });
        services.AddZendiator();
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        for (var i = 0; i < 3; i++)
            Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(1, PipeB0.FactoryCalls);
        Assert.Equal(1, PipeB1.FactoryCalls);
        Assert.Equal(3, PipeB2.HandleCalls);
        Assert.Equal(1, PipeHandler.Constructions);
    }

    [Fact]
    public async Task Same_reference_transient_factory_is_captured_once()
    {
        var services = BaseServices();
        var first = new ProbeHandler { Marker = 7 };
        var calls = 0;
        services.AddTransient<ProbeHandler>(_ =>
        {
            Interlocked.Increment(ref calls);
            return first;
        });
        services.AddZendiator();
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        for (var i = 0; i < 4; i++)
            Assert.Equal(7, await mediator.SendAsync(new ProbeReq()));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Transient_mediators_each_capture_the_factory_result()
    {
        var services = BaseServices();
        var first = new ProbeHandler { Marker = 7 };
        var calls = 0;
        services.AddTransient<ProbeHandler>(_ =>
        {
            var c = Interlocked.Increment(ref calls);
            return c <= 2 ? first : new ProbeHandler { Marker = c };
        });
        services.AddZendiator(ServiceLifetime.Transient);
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        foreach (var expected in new[] { 7, 7, 3, 4 })
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            Assert.Equal(expected, await mediator.SendAsync(new ProbeReq()));
            Assert.Equal(expected, await mediator.SendAsync(new ProbeReq()));
        }
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task Pre_registered_transient_wins()
    {
        var services = BaseServices();
        services.AddTransient<Guid0Handler>(_ =>
        {
            Interlocked.Increment(ref Guid0Handler.FactoryCalls);
            return new Guid0Handler();
        });
        services.AddZendiator();
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var a = await mediator.SendAsync(new Guid0());
        var b = await mediator.SendAsync(new Guid0());
        var c = await mediator.SendAsync(new Guid0());
        Assert.Equal(a, b);
        Assert.Equal(b, c);
        Assert.Equal(1, Guid0Handler.FactoryCalls);
        using var nextScope = provider.CreateScope();
        Assert.NotEqual(a, await nextScope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(new Guid0()));
        Assert.Equal(2, Guid0Handler.FactoryCalls);
    }

    [Fact]
    public async Task Post_registered_add_transient_wins()
    {
        var services = BaseServices();
        services.AddZendiator();
        services.AddTransient<Guid0Handler>(_ =>
        {
            Interlocked.Increment(ref Guid0Handler.FactoryCalls);
            return new Guid0Handler();
        });
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var a = await mediator.SendAsync(new Guid0());
        var b = await mediator.SendAsync(new Guid0());
        Assert.Equal(a, b);
        Assert.Equal(1, Guid0Handler.FactoryCalls);
    }

    [Fact]
    public async Task Post_registered_tryadd_does_not_override()
    {
        var services = BaseServices();
        services.AddZendiator();
        services.TryAddTransient<Guid0Handler>(_ =>
        {
            Interlocked.Increment(ref Guid0Handler.FactoryCalls);
            return new Guid0Handler();
        });
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var a = await mediator.SendAsync(new Guid0());
        var b = await mediator.SendAsync(new Guid0());
        Assert.Equal(a, b);
        Assert.Equal(0, Guid0Handler.FactoryCalls);
    }

    [Fact]
    public async Task Throwing_factory_publishes_no_partial_state()
    {
        var services = BaseServices();
        var calls = 0;
        services.AddScoped<Guid0Handler>(_ =>
        {
            var c = Interlocked.Increment(ref calls);
            if (c == 1)
                throw new InvalidOperationException("first init fails");
            return new Guid0Handler();
        });
        services.AddZendiator();
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await mediator.SendAsync(new Guid0()));
        var second = await mediator.SendAsync(new Guid0());
        Assert.Equal(second, await mediator.SendAsync(new Guid0()));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Closed_behaviors_apply_only_to_matching_request()
    {
        var services = BaseServices();
        services.AddZendiator();
        await using var provider = await BuildAsync(services);
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
        Assert.Equal(0, PipeB0.HandleCalls);
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(1, PipeB0.HandleCalls);
        Assert.Equal(1, PipeB1.HandleCalls);
        Assert.Equal(1, PipeB2.HandleCalls);
    }
}
