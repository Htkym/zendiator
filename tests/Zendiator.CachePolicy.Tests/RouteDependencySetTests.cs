using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.DependencyInjection;

namespace Zendiator.CachePolicy.Tests;

public sealed class RouteDependencySetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_resolution_shares_route_set_captures(bool directFirst)
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        ZendiatorServiceResolver asBase = (Zendiator)mediator;
        var direct = directFirst ? asBase.GetRequiredService<PipeB1>() : null;
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        direct ??= asBase.GetRequiredService<PipeB1>();
        Assert.Same(direct, ((Zendiator)mediator).GetRequiredService<PipeB1>());
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(1, PipeB1.Constructions);
        Assert.Equal(1, PipeHandler.Constructions);
        Assert.Equal(2, PipeB1.HandleCalls);
    }

    [Fact]
    public async Task Concurrent_first_use_of_a_route_set_captures_each_dependency_once()
    {
        TestCounters.ResetAll();
        var services = new ServiceCollection();
        services.AddTransient(_ => { Interlocked.Increment(ref PipeB0.FactoryCalls); return new PipeB0(); });
        services.AddTransient(_ => { Interlocked.Increment(ref PipeB2.FactoryCalls); return new PipeB2(); });
        services.AddTransient(_ => { Interlocked.Increment(ref PipeHandler.FactoryCalls); return new PipeHandler(); });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sends = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await mediator.SendAsync(new PipeReq(1));
        })).ToArray();
        start.SetResult();
        Assert.All(await Task.WhenAll(sends), result => Assert.Equal(1001, result));
        Assert.Equal(1, PipeB0.FactoryCalls);
        Assert.Equal(1, PipeB1.Constructions);
        Assert.Equal(1, PipeB2.FactoryCalls);
        Assert.Equal(1, PipeHandler.FactoryCalls);
    }

    [Fact]
    public async Task Waiters_retry_after_a_failed_route_set_activation()
    {
        TestCounters.ResetAll();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var services = new ServiceCollection();
        services.AddTransient(_ =>
        {
            if (Interlocked.Increment(ref PipeB1.FactoryCalls) == 1)
            {
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
                throw new FormatException("activation");
            }
            return new PipeB1();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var first = Task.Factory.StartNew(() => Assert.Throws<FormatException>(() => mediator.SendAsync(new PipeReq(1)).AsTask().GetAwaiter().GetResult()),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var waiters = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
        {
            if (Interlocked.Increment(ref started) == 8) ready.SetResult();
            return mediator.SendAsync(new PipeReq(1)).AsTask().GetAwaiter().GetResult();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(10)), result => Assert.Equal(1001, result));
            Assert.Equal(2, PipeB1.FactoryCalls);
            Assert.Equal(1, PipeB1.Constructions);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Waiters_retry_after_the_first_dependency_fails_while_its_set_is_created()
    {
        TestCounters.ResetAll();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var services = new ServiceCollection();
        services.AddTransient(_ =>
        {
            if (Interlocked.Increment(ref PipeB0.FactoryCalls) == 1)
            {
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
                throw new FormatException("activation");
            }
            return new PipeB0();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var first = Task.Factory.StartNew(() => Assert.Throws<FormatException>(() => mediator.SendAsync(new PipeReq(1)).AsTask().GetAwaiter().GetResult()),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var waiters = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(() =>
        {
            if (Interlocked.Increment(ref started) == 8) ready.SetResult();
            return mediator.SendAsync(new PipeReq(1)).AsTask().GetAwaiter().GetResult();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(10)), result => Assert.Equal(1001, result));
            Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
            Assert.Equal(2, PipeB0.FactoryCalls);
            Assert.Equal(1, PipeB0.Constructions);
            Assert.Equal(1, PipeB1.Constructions);
            Assert.Equal(1, PipeHandler.Constructions);
            Assert.Equal(9, PipeB0.HandleCalls);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task First_dependency_factory_can_reenter_another_route_while_its_set_is_created()
    {
        TestCounters.ResetAll();
        IZendiator? mediator = null;
        var services = new ServiceCollection();
        services.AddTransient(_ =>
        {
            Interlocked.Increment(ref PipeB0.FactoryCalls);
            Assert.Equal(7, mediator!.SendAsync(new GateReq(true)).AsTask().GetAwaiter().GetResult());
            return new PipeB0();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(7, await mediator.SendAsync(new GateReq(true)));
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(1, PipeB0.FactoryCalls);
        Assert.Equal(1, PipeB0.Constructions);
        Assert.Equal(1, GateHandler.Constructions);
        Assert.Equal(2, GateBehavior.HandleCalls);
        Assert.Equal(2, PipeB0.HandleCalls);
    }

    [Fact]
    public async Task First_dependency_factory_can_resolve_a_later_member_of_the_same_set()
    {
        TestCounters.ResetAll();
        Zendiator? mediator = null;
        PipeHandler? early = null;
        var services = new ServiceCollection();
        services.AddTransient(_ =>
        {
            Interlocked.Increment(ref PipeB0.FactoryCalls);
            early = ((ZendiatorServiceResolver)mediator!).GetRequiredService<PipeHandler>();
            return new PipeB0();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        mediator = (Zendiator)scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.NotNull(early);
        Assert.Same(early, mediator.GetRequiredService<PipeHandler>());
        Assert.Equal(1, PipeB0.FactoryCalls);
        Assert.Equal(1, PipeHandler.Constructions);
        Assert.Equal(2, PipeB0.HandleCalls);
    }

    [Fact]
    public async Task Factory_reentrancy_publishes_another_route_set_first()
    {
        TestCounters.ResetAll();
        IZendiator? mediator = null;
        var services = new ServiceCollection();
        services.AddTransient(_ =>
        {
            Assert.Equal(7, mediator!.SendAsync(new GateReq(true)).AsTask().GetAwaiter().GetResult());
            return new PipeB1();
        });
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(7, await mediator.SendAsync(new GateReq(true)));
        Assert.Equal(1001, await mediator.SendAsync(new PipeReq(1)));
        Assert.Equal(1, PipeB1.Constructions);
        Assert.Equal(1, GateHandler.Constructions);
        Assert.Equal(2, GateBehavior.HandleCalls);
    }

    [Fact]
    public async Task General_resolver_waiters_retry_after_a_failed_activation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var services = new ServiceCollection();
        services.AddTransient(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
                throw new FormatException("activation");
            }
            return new Slow();
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var resolver = new ZendiatorServiceResolver<FailureComposition>(scope.ServiceProvider);
        var first = Task.Factory.StartNew(() => Assert.Throws<FormatException>(resolver.GetRequiredService<Slow>),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var waiters = Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(resolver.GetRequiredService<Slow>,
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            var results = await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(results, result => Assert.Same(results[0], result));
            Assert.Equal(2, calls);
        }
        finally
        {
            release.Set();
        }
    }

    private sealed class FailureComposition;
    private sealed class Slow;
}
