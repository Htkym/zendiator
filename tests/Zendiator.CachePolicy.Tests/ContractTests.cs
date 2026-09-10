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

public sealed class LazyAndPipelineMeaningTests
{
    [Fact]
    public async Task Token_substitution_survives_warmup()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddScoped<TokHandler>(_ =>
        {
            Interlocked.Increment(ref TokHandler.FactoryCalls);
            return new TokHandler();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        using var live = new CancellationTokenSource();
        Assert.Equal(live.Token, await mediator.SendAsync(new TokReq(live.Token)));
        Assert.Equal(live.Token, await mediator.SendAsync(new TokReq(live.Token)));
        Assert.Equal(1, TokHandler.FactoryCalls);
        using var dead = new CancellationTokenSource();
        dead.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await mediator.SendAsync(new TokReq(dead.Token)));
    }

    [Fact]
    public async Task Request_transform_reaches_downstream()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(15, await mediator.SendAsync(new AddReq(5)));
        var handler = scope.ServiceProvider.GetRequiredService<AddHandler>();
        Assert.Equal(15, handler.Seen);
    }

    [Fact]
    public async Task Null_downstream_still_throws_argument_null()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await mediator.SendAsync(new RefReq("a")));
        Assert.Equal(0, RefHandler.Constructions);
    }

    [Fact]
    public async Task Short_circuit_builds_nothing_then_resolves_lazily()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddScoped<GateHandler>(_ =>
        {
            Interlocked.Increment(ref GateHandler.FactoryCalls);
            return new GateHandler();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(-1, await mediator.SendAsync(new GateReq(false)));
        Assert.Equal(0, GateHandler.FactoryCalls);
        Assert.Equal(7, await mediator.SendAsync(new GateReq(true)));
        Assert.Equal(1, GateHandler.FactoryCalls);
        Assert.Equal(7, await mediator.SendAsync(new GateReq(true)));
        Assert.Equal(1, GateHandler.FactoryCalls);
    }

    [Fact]
    public async Task Retry_scoped_constructs_once_handles_twice()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(2, await mediator.SendAsync(new RetryReq()));
        Assert.Equal(1, RetryHandler.Constructions);
        Assert.Equal(1, RetryTwiceBehavior.HandleCalls);
    }

    [Fact]
    public async Task Retry_transient_constructs_per_invocation()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddTransient<RetryHandler>(_ =>
        {
            Interlocked.Increment(ref RetryHandler.FactoryCalls);
            return new RetryHandler();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(1, await mediator.SendAsync(new RetryReq()));
        Assert.Equal(2, RetryHandler.FactoryCalls);
        Assert.Equal(1, await mediator.SendAsync(new RetryReq()));
        Assert.Equal(4, RetryHandler.FactoryCalls);
    }

    [Fact]
    public async Task Explicit_implementations_work_warm()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(42, await mediator.SendAsync(new ExpReq(41)));
            Assert.Equal(42, await mediator.SendAsync(new ExpPipeReq(41)));
        }
        Assert.Equal(3, ExpPipeBehavior.HandleCalls);
    }

    [Fact]
    public async Task Suspending_sends_complete_warm()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(42, await mediator.SendAsync(new SuspReq(41)));
            Assert.Equal(42, await mediator.SendAsync(new SuspPipeReq(41)));
        }
        Assert.Equal(3, SuspBehavior.HandleCalls);
        Assert.Equal(3, SuspBehavior.FinallyCalls);
    }

    [Fact]
    public async Task Exceptions_finally_and_precancel_keep_meaning_warm()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddScoped<FailHandler>(_ =>
        {
            Interlocked.Increment(ref FailHandler.FactoryCalls);
            return new FailHandler();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Same(CacheFixtureErrors.Instance,
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await mediator.SendAsync(new FailReq(1))));
        Assert.Same(CacheFixtureErrors.Instance,
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await mediator.SendAsync(new FailReq(1))));
        Assert.Equal(2, FailFinallyBehavior.FinallyCalls);
        using var dead = new CancellationTokenSource();
        dead.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await mediator.SendAsync(new FailReq(1), dead.Token));
        Assert.Equal(1, FailHandler.FactoryCalls);
    }
}

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
