using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Zendiator.CachePolicy.Tests;

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
