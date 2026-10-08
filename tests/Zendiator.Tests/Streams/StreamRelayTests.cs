using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

// Behaviors that pre-process and return next's sequence without their own async iterator.
public sealed class StreamRelayTests
{
    [Fact]
    public async Task Relay_behaviors_run_on_first_move_in_pipeline_order()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var trace = scope.ServiceProvider.GetRequiredService<Trace>();
        var stream = mediator.StreamAsync(new GetRelay(2));
        await using (var e = stream.GetAsyncEnumerator())
        {
            Assert.Empty(trace.Events);
            Assert.True(await e.MoveNextAsync());
            Assert.Equal(["ra", "rb", "rh"], trace.Events);
            Assert.Equal(0, e.Current);
            Assert.True(await e.MoveNextAsync());
            Assert.Equal(1, e.Current);
            Assert.False(await e.MoveNextAsync());
        }
        Assert.Equal(["ra", "rb", "rh", "/rh"], trace.Events);
        trace.Events.Clear();
        Assert.Equal([0, 1], await Collect(stream));
        Assert.Equal(["ra", "rb", "rh", "/rh"], trace.Events);
        trace.Events.Clear();
        _ = mediator.StreamAsync(new GetRelay(2));
        Assert.Empty(trace.Events);
    }

    [Fact]
    public async Task Relay_failure_surfaces_on_first_move_without_starting_the_handler()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var trace = scope.ServiceProvider.GetRequiredService<Trace>();
        var e = mediator.StreamAsync(new GetRelay(2, FailBefore: true)).GetAsyncEnumerator();
        Assert.Empty(trace.Events);
        var pending = e.MoveNextAsync();
        Assert.Same(StreamRelayB.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending));
        Assert.Equal(["ra", "rb"], trace.Events);
        Assert.False(await e.MoveNextAsync());
        await e.DisposeAsync();
        Assert.False(await e.MoveNextAsync());
        Assert.Equal(["ra", "rb"], trace.Events);
    }

    [Fact]
    public async Task Mixed_relay_and_iterator_behaviors_keep_order_and_cleanup()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var trace = scope.ServiceProvider.GetRequiredService<Trace>();
        Assert.Equal([0, 1, 2], await Collect(mediator.StreamAsync(new GetRelayMixed(3))));
        Assert.Equal(["mo", "mi", "mn", "mh", "/mh", "/mi"], trace.Events);

        trace.Events.Clear();
        var seen = new List<int>();
        Assert.Same(GetRelayMixedHandler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var i in mediator.StreamAsync(new GetRelayMixed(3, FailAt: 1))) seen.Add(i);
        }));
        Assert.Equal([0], seen);
        Assert.Equal(["mo", "mi", "mn", "mh", "/mh", "/mi"], trace.Events);

        trace.Events.Clear();
        await foreach (var _ in mediator.StreamAsync(new GetRelayMixed(10))) break;
        Assert.Equal(["mo", "mi", "mn", "mh", "/mh", "/mi"], trace.Events);
    }

    [Fact]
    public async Task Relay_pipeline_observes_api_and_enumerator_tokens()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var trace = scope.ServiceProvider.GetRequiredService<Trace>();
        using var pre = new CancellationTokenSource();
        pre.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Collect(mediator.StreamAsync(new GetRelay(3), pre.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in mediator.StreamAsync(new GetRelay(3)).WithCancellation(pre.Token)) { }
        });
        Assert.Empty(trace.Events);

        using var mid = new CancellationTokenSource();
        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in mediator.StreamAsync(new GetRelay(100)).WithCancellation(mid.Token))
                if (++count == 2) mid.Cancel();
        });
        Assert.Equal(2, count);
        Assert.Equal(mid.Token, GetRelayHandler.Seen);

        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        await using var e = mediator.StreamAsync(new GetRelay(100), api.Token).GetAsyncEnumerator(enumeration.Token);
        Assert.True(await e.MoveNextAsync());
        Assert.NotEqual(api.Token, GetRelayHandler.Seen);
        Assert.NotEqual(enumeration.Token, GetRelayHandler.Seen);
        api.Cancel();
        Assert.True(GetRelayHandler.Seen.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await e.MoveNextAsync());
        Assert.False(await e.MoveNextAsync());
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

    private static async Task<List<int>> Collect(IAsyncEnumerable<int> stream)
    {
        var list = new List<int>();
        await foreach (var i in stream) list.Add(i);
        return list;
    }
}
