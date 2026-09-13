using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

// Covers the enumerator lifecycle of the generated StreamRoute{N}Enumerator,
// in particular the P0 split of first-call startup (StartAndMoveNextAsync)
// and steady-state forwarding MoveNextAsync.
public sealed class StreamLifecycleTests
{
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
    public async Task Single_item_stream_completes_and_stays_completed()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await using var e = mediator.StreamAsync(new GetOne(7)).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(7, e.Current);
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

    [Fact]
    public async Task Current_before_first_move_next_is_default()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await using var e = mediator.StreamAsync(new LifecycleNumbers(3)).GetAsyncEnumerator();
        Assert.Equal(0, e.Current);
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(0, e.Current);
    }
}
