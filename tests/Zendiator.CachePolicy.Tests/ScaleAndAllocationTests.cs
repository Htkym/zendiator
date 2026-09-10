using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

public sealed class ScaleAndAllocationTests
{
    private static void AddCountingFactories(ServiceCollection services, ServiceLifetime lifetime)
    {
        void Add<T>(Func<IServiceProvider, T> factory) where T : class
        {
            var descriptor = ServiceDescriptor.Describe(typeof(T), sp => factory(sp), lifetime);
            services.Add(descriptor);
        }

        Add<Val0Handler>(sp => { Interlocked.Increment(ref Val0Handler.FactoryCalls); return new Val0Handler(); });
        Add<Guid0Handler>(sp => { Interlocked.Increment(ref Guid0Handler.FactoryCalls); return new Guid0Handler(); });
        Add<ProbeHandler>(sp => { Interlocked.Increment(ref ProbeHandler.FactoryCalls); return new ProbeHandler(); });
        Add<PipeHandler>(sp => { Interlocked.Increment(ref PipeHandler.FactoryCalls); return new PipeHandler(); });
        Add<PipeB0>(sp => { Interlocked.Increment(ref PipeB0.FactoryCalls); return new PipeB0(); });
        Add<PipeB1>(sp => { Interlocked.Increment(ref PipeB1.FactoryCalls); return new PipeB1(); });
        Add<PipeB2>(sp => { Interlocked.Increment(ref PipeB2.FactoryCalls); return new PipeB2(); });
        Add<GateHandler>(sp => { Interlocked.Increment(ref GateHandler.FactoryCalls); return new GateHandler(); });
        Add<RetryHandler>(sp => { Interlocked.Increment(ref RetryHandler.FactoryCalls); return new RetryHandler(); });
        Add<TokHandler>(sp => { Interlocked.Increment(ref TokHandler.FactoryCalls); return new TokHandler(); });
        Add<AddHandler>(sp => { Interlocked.Increment(ref AddHandler.FactoryCalls); return new AddHandler(); });
        Add<RefHandler>(sp => { Interlocked.Increment(ref RefHandler.FactoryCalls); return new RefHandler(); });
        Add<ExpHandler>(sp => { Interlocked.Increment(ref ExpHandler.FactoryCalls); return new ExpHandler(); });
        Add<ExpPipeHandler>(sp => { Interlocked.Increment(ref ExpPipeHandler.FactoryCalls); return new ExpPipeHandler(); });
        Add<SuspHandler>(sp => { Interlocked.Increment(ref SuspHandler.FactoryCalls); return new SuspHandler(); });
        Add<SuspPipeHandler>(sp => { Interlocked.Increment(ref SuspPipeHandler.FactoryCalls); return new SuspPipeHandler(); });
        Add<FailHandler>(sp => { Interlocked.Increment(ref FailHandler.FactoryCalls); return new FailHandler(); });
        Add<DispHandler>(sp => { Interlocked.Increment(ref DispHandler.FactoryCalls); return new DispHandler(); });
        Add<ReHandler>(sp => { Interlocked.Increment(ref ReHandler.FactoryCalls); return new ReHandler(); });
    }

    [Fact]
    public async Task Unused_routes_are_never_constructed()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        AddCountingFactories(services, ServiceLifetime.Scoped);
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        for (var i = 0; i < 3; i++)
            Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
        Assert.Equal(1, Val0Handler.FactoryCalls);
        Assert.Equal(0, Guid0Handler.FactoryCalls);
        Assert.Equal(0, ProbeHandler.FactoryCalls);
        Assert.Equal(0, PipeHandler.FactoryCalls);
        Assert.Equal(0, PipeB0.FactoryCalls);
        Assert.Equal(0, PipeB1.FactoryCalls);
        Assert.Equal(0, PipeB2.FactoryCalls);
        Assert.Equal(0, GateHandler.FactoryCalls);
        Assert.Equal(0, RetryHandler.FactoryCalls);
        Assert.Equal(0, TokHandler.FactoryCalls);
        Assert.Equal(0, AddHandler.FactoryCalls);
        Assert.Equal(0, RefHandler.FactoryCalls);
        Assert.Equal(0, ExpHandler.FactoryCalls);
        Assert.Equal(0, ExpPipeHandler.FactoryCalls);
        Assert.Equal(0, SuspHandler.FactoryCalls);
        Assert.Equal(0, SuspPipeHandler.FactoryCalls);
        Assert.Equal(0, FailHandler.FactoryCalls);
        Assert.Equal(0, DispHandler.FactoryCalls);
        Assert.Equal(0, ReHandler.FactoryCalls);
        Assert.Equal(0, PipeB0.HandleCalls);
    }

    [Fact]
    public async Task Only_constructed_services_are_disposed()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        AddCountingFactories(services, ServiceLifetime.Scoped);
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
        Assert.Equal(5, await mediator.SendAsync(new DispReq()));
        await scope.DisposeAsync();
        Assert.Equal(1, DispHandler.DisposeCount);
        await provider.DisposeAsync();
    }

    private static long Measure(Action action)
    {
        action();
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1024; i++)
            action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public async Task Warm_paths_allocate_nothing()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(42, await mediator.SendAsync(new Val0(41)));
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(0, Measure(() => _ = mediator.SendAsync(new Val0(41)).GetAwaiter().GetResult()));
        Assert.Equal(0, Measure(() => _ = mediator.SendAsync(new PipeReq(1)).GetAwaiter().GetResult()));
        await scope.DisposeAsync();
        await provider.DisposeAsync();
    }
}
