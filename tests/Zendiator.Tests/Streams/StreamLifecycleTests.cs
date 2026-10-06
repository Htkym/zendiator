using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

// Covers the enumerator lifecycle of the generated StreamRoute{N}Enumerable,
// in particular the P0 split of first-call startup (StartAndMoveNextAsync)
// and steady-state forwarding MoveNextAsync.
public sealed class StreamLifecycleTests
{
    [Fact]
    public async Task Null_request_is_rejected_on_first_move_with_the_existing_parameter_name()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await using var enumerator = mediator.StreamAsync((LifecycleNumbers)null!).GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync();
        var error = await Assert.ThrowsAsync<ArgumentNullException>(async () => await pending);
        Assert.Equal("_request", error.ParamName);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_startup_is_not_retried(bool cancel)
    {
        var services = Services();
        var calls = 0;
        var failure = new InvalidOperationException("factory");
        services.AddTransient<LifecycleNumbersHandler>(_ => { calls++; throw failure; });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel();
        await using var e = scope.ServiceProvider.GetRequiredService<IZendiator>()
            .StreamAsync(new LifecycleNumbers(1), cancellation.Token).GetAsyncEnumerator();
        var pending = e.MoveNextAsync();
        if (cancel)
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending)).CancellationToken);
        else
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending));
        Assert.False(await e.MoveNextAsync());
        Assert.False(await e.MoveNextAsync());
        Assert.Equal(cancel ? 0 : 1, calls);
    }

    [Fact]
    public async Task Enumerators_from_one_stream_have_independent_state_and_tokens()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        using var cancellation = new CancellationTokenSource();
        var stream = mediator.StreamAsync(new LifecycleNumbers(3));
        await using var first = stream.GetAsyncEnumerator(cancellation.Token);
        await using var second = stream.GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.Equal(0, first.Current);
        Assert.Equal(0, second.Current);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first.MoveNextAsync());
        await first.DisposeAsync();
        Assert.True(await second.MoveNextAsync());
        Assert.Equal(1, second.Current);
        await second.DisposeAsync();
        await using var third = stream.GetAsyncEnumerator();
        Assert.True(await third.MoveNextAsync());
        Assert.Equal(0, third.Current);
    }

    [Fact]
    public async Task Enumerator_requested_on_another_thread_stays_independent()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var stream = scope.ServiceProvider.GetRequiredService<IZendiator>().StreamAsync(new LifecycleNumbers(2));
        var other = await Task.Factory.StartNew(() => stream.GetAsyncEnumerator(),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await using var own = stream.GetAsyncEnumerator();
        Assert.NotSame(other, own);
        Assert.True(await other.MoveNextAsync());
        Assert.True(await other.MoveNextAsync());
        Assert.True(await own.MoveNextAsync());
        Assert.Equal(1, other.Current);
        Assert.Equal(0, own.Current);
        await other.DisposeAsync();
        Assert.True(await own.MoveNextAsync());
        Assert.False(await own.MoveNextAsync());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        services.AddScoped<AuditLog>();
        services.AddScoped<Gate>();
        services.AddZendiator();
        return services;
    }

    [Fact]
    public async Task Empty_stream_completes_and_stays_completed()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await using var e = mediator.StreamAsync(new LifecycleNumbers(0)).GetAsyncEnumerator();
        Assert.False(await e.MoveNextAsync());
        Assert.False(await e.MoveNextAsync());
    }

    [Fact]
    public async Task Completed_stream_with_behavior_stays_completed()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await using var e = mediator.StreamAsync(new GetPiped(2)).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        Assert.True(await e.MoveNextAsync());
        Assert.False(await e.MoveNextAsync());
        Assert.False(await e.MoveNextAsync());
    }

    [Fact]
    public async Task Move_next_after_failure_returns_false()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await using var e = mediator.StreamAsync(new GetFail(3, 1)).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(0, e.Current);
        Assert.Same(GetFailHandler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await e.MoveNextAsync()));
        Assert.False(await e.MoveNextAsync());
    }

    [Fact]
    public async Task Dispose_before_first_move_next_never_starts_handler()
    {
        LifecycleNumbersHandler.Starts = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var e = mediator.StreamAsync(new LifecycleNumbers(3)).GetAsyncEnumerator();
        await e.DisposeAsync();
        Assert.Equal(0, LifecycleNumbersHandler.Starts);
        Assert.False(await e.MoveNextAsync());
    }

    [Fact]
    public async Task Dispose_after_cancellation_does_not_throw()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        using var cts = new CancellationTokenSource();
        var e = mediator.StreamAsync(new LifecycleNumbers(100), cts.Token).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await e.MoveNextAsync());
        await e.DisposeAsync();
        Assert.False(await e.MoveNextAsync());
    }
}
