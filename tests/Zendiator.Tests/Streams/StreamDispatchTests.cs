using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class StreamDispatchTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Disposal_preserves_context_and_captures_synchronous_exceptions(bool throws, bool linked)
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var context = new AsyncLocal<string?> { Value = "caller" };
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("dispose failed");
        var calls = 0;
        var request = new ThrowingDisposeStream(() =>
        {
            context.Value = "inner";
            calls++;
            if (throws) throw failure;
            return new ValueTask(completion.Task);
        });
        var e = scope.ServiceProvider.GetRequiredService<IZendiator>()
            .StreamAsync(request, linked ? api.Token : default).GetAsyncEnumerator(linked ? enumeration.Token : default);
        Assert.True(await e.MoveNextAsync());
        var pending = e.DisposeAsync();
        Assert.Equal("caller", context.Value);
        if (throws)
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending));
        else
        {
            Assert.False(pending.IsCompleted);
            completion.SetResult();
            await pending;
        }
        await e.DisposeAsync();
        Assert.Equal(1, calls);
        api.Cancel();
        enumeration.Cancel();
        Assert.False(scope.ServiceProvider.GetRequiredService<ThrowingDisposeStreamHandler>().Token.IsCancellationRequested);
        Assert.False(await e.MoveNextAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposal_after_start_failure_detaches_linked_cancellation(bool failOnGetEnumerator)
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var e = mediator.StreamAsync(new ThrowingDisposeStream(FailOnStart: !failOnGetEnumerator, FailOnGetEnumerator: failOnGetEnumerator), api.Token).GetAsyncEnumerator(enumeration.Token);
        var pending = e.MoveNextAsync();
        var handler = scope.ServiceProvider.GetRequiredService<ThrowingDisposeStreamHandler>();
        Assert.Same(handler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending));
        Assert.False(await e.MoveNextAsync());
        Assert.False(await e.MoveNextAsync());
        await e.DisposeAsync();
        api.Cancel();
        enumeration.Cancel();
        Assert.False(handler.Token.IsCancellationRequested);
        await e.DisposeAsync();
        Assert.False(await e.MoveNextAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopped_startup_still_disposes_inner_and_linked_token(bool fail)
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("first move");
        var moves = 0;
        var disposals = 0;
        var request = new ThrowingDisposeStream(
            DisposeCallback: () => { disposals++; return new(disposal.Task); },
            MoveNextCallback: () => { moves++; return new(completion.Task); });
        var e = scope.ServiceProvider.GetRequiredService<IZendiator>().StreamAsync(request, api.Token).GetAsyncEnumerator(enumeration.Token);
        var pending = e.MoveNextAsync();
        Assert.False(pending.IsCompleted);
        if (fail)
        {
            completion.SetException(failure);
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending));
        }
        else
        {
            completion.SetResult(false);
            Assert.False(await pending);
        }
        Assert.False(await e.MoveNextAsync());
        Assert.Equal(1, moves);
        Assert.Equal(0, disposals);
        var dispose = e.DisposeAsync();
        Assert.False(dispose.IsCompleted);
        Assert.False(await e.MoveNextAsync());
        disposal.SetResult();
        await dispose;
        await e.DisposeAsync();
        Assert.Equal(1, disposals);
        api.Cancel();
        enumeration.Cancel();
        Assert.False(scope.ServiceProvider.GetRequiredService<ThrowingDisposeStreamHandler>().Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Subsequent_moves_forward_the_original_value_task_once()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var completion = new TaskCompletionSource<bool>();
        var expected = new ValueTask<bool>(completion.Task);
        var calls = 0;
        var request = new ThrowingDisposeStream(DisposeCallback: () => default,
            MoveNextCallback: () => ++calls == 1 ? new(true) : expected);
        await using var e = scope.ServiceProvider.GetRequiredService<IZendiator>().StreamAsync(request).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        var actual = e.MoveNextAsync();
        Assert.Equal(expected, actual);
        Assert.Equal(2, calls);
        completion.SetResult(false);
        Assert.False(await actual);
    }

    [Fact]
    public async Task Throwing_inner_disposal_still_detaches_linked_cancellation()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var e = mediator.StreamAsync(new ThrowingDisposeStream(), api.Token).GetAsyncEnumerator(enumeration.Token);
        Assert.True(await e.MoveNextAsync());
        var handler = scope.ServiceProvider.GetRequiredService<ThrowingDisposeStreamHandler>();
        Assert.Same(handler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await e.DisposeAsync()));
        api.Cancel();
        Assert.False(handler.Token.IsCancellationRequested);
        await e.DisposeAsync();
        Assert.False(await e.MoveNextAsync());
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        services.AddScoped<AuditLog>();
        services.AddScoped<Gate>();
        services.AddScoped<ThrowingDisposeStreamHandler>();
        services.AddZendiator();
        return services;
    }

    private static async Task<List<int>> Collect(IAsyncEnumerable<int> stream)
    {
        var list = new List<int>();
        await foreach (var i in stream) list.Add(i);
        return list;
    }

    [Fact]
    public async Task S_basic_counts_and_order()
    {
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Empty(await Collect(mediator.StreamAsync(new GetNumbers(0))));
        Assert.Equal([7], await Collect(mediator.StreamAsync(new GetOne(7))));
        Assert.Equal(Enumerable.Range(0, 16), await Collect(mediator.StreamAsync(new GetNumbers(16))));
    }

    [Fact]
    public async Task L_lazy_execution()
    {
        GetLazyHandler.Started = 0;
        GetLazyHandler.Items = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var stream = mediator.StreamAsync(new GetLazy(3));
        Assert.Equal(0, GetLazyHandler.Started);
        var enumerator = stream.GetAsyncEnumerator();
        Assert.Equal(0, GetLazyHandler.Started);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, GetLazyHandler.Started);
        await enumerator.DisposeAsync();
        Assert.Equal([0, 1, 2], await Collect(stream));
        Assert.Equal(2, GetLazyHandler.Started);
    }

    [Fact]
    public async Task P_pipeline_depths_and_order()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var trace = scope.ServiceProvider.GetRequiredService<Trace>();
        trace.Events.Clear();
        Assert.Equal([0, 1], await Collect(mediator.StreamAsync(new GetPiped(2))));
        Assert.Equal(["spipe", "/spipe"], trace.Events);
        trace.Events.Clear();
        Assert.Equal([0, 1], await Collect(mediator.StreamAsync(new GetPiped3(2))));
        Assert.Equal(["a3", "b3", "c3", "/c3", "/b3", "/a3"], trace.Events);
        trace.Events.Clear();
        Assert.Equal([0, 1], await Collect(mediator.StreamAsync(new GetPiped5(2))));
        Assert.Equal(["a5", "b5", "c5", "d5", "e5", "/e5", "/d5", "/c5", "/b5", "/a5"], trace.Events);
    }

    [Fact]
    public async Task P_transform_filter_shortcircuit_replace()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal([0, 10, 20], await Collect(mediator.StreamAsync(new GetTransform(3))));
        Assert.Equal([0, 2, 4], await Collect(mediator.StreamAsync(new GetFilter(6))));
        GetGatedHandler.Calls = 0;
        Assert.Empty(await Collect(mediator.StreamAsync(new GetGated(false, 4))));
        Assert.Equal(0, GetGatedHandler.Calls);
        Assert.Equal(3, (await Collect(mediator.StreamAsync(new GetGated(true, 3)))).Count);
        GetReplacedHandler.Seen = -1;
        var replaced = await Collect(mediator.StreamAsync(new GetReplaced(2)));
        Assert.Equal(7, replaced.Count);
        Assert.Equal(7, GetReplacedHandler.Seen);
    }

    [Fact]
    public async Task P_finally_on_complete_error_and_break()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var trace = scope.ServiceProvider.GetRequiredService<Trace>();
        trace.Events.Clear();
        Assert.Equal([0, 1], await Collect(mediator.StreamAsync(new GetFail(2, 99))));
        Assert.Contains("fail-before", trace.Events);
        Assert.Contains("fail-after", trace.Events);
        Assert.Contains("/fail", trace.Events);
        trace.Events.Clear();
        var error = GetFailHandler.Failure;
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await Collect(mediator.StreamAsync(new GetFail(4, 2)))));
        Assert.Contains("/fail", trace.Events);
        // Early break still runs finally.
        trace.Events.Clear();
        await using (var e = mediator.StreamAsync(new GetPiped3(10)).GetAsyncEnumerator())
        {
            Assert.True(await e.MoveNextAsync());
        }
        Assert.Contains("/a3", trace.Events);
    }

    [Fact]
    public async Task C_cancellation_paths()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        // Pre-cancel via API token.
        using var pre = new CancellationTokenSource();
        pre.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Collect(mediator.StreamAsync(new GetCancel(3), pre.Token)));
        // Pre-cancel via enumerator token.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in mediator.StreamAsync(new GetCancel(3)).WithCancellation(pre.Token)) { }
        });
        // Cancel between items via API token linked at start.
        using var mid = new CancellationTokenSource();
        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in mediator.StreamAsync(new GetCancel(100), mid.Token))
            {
                if (++count == 2) mid.Cancel();
            }
        });
        // Same token for both paths.
        using var same = new CancellationTokenSource();
        var stream = mediator.StreamAsync(new GetCancel(2), same.Token);
        var collected = new List<int>();
        await foreach (var i in stream.WithCancellation(same.Token)) collected.Add(i);
        Assert.Equal([0, 1], collected);
        // Different tokens: either cancels.
        using var api = new CancellationTokenSource();
        using var en = new CancellationTokenSource();
        en.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in mediator.StreamAsync(new GetCancel(3), api.Token).WithCancellation(en.Token)) { }
        });
    }

    [Fact]
    public async Task G_generic_closed_open()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var ints = new List<int>();
        await foreach (var i in mediator.StreamAsync(new StreamEntities<int>(3))) ints.Add(i);
        Assert.Equal(3, ints.Count);
        var entities = new List<StreamEntity>();
        await foreach (var s in mediator.StreamAsync(new StreamEntities<StreamEntity>(2))) entities.Add(s);
        Assert.Equal(2, entities.Count);
    }

    [Fact]
    public async Task Transient_stream_factory_is_used_and_disposed_scope_rejects_lazy_startup()
    {
        // Transient factory override is respected (TryAdd does not replace).
        var factoryServices = Services();
        var factoryCalls = 0;
        factoryServices.AddTransient<GetOneHandler>(_ =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new GetOneHandler();
        });
        await using var factoryProvider = factoryServices.BuildServiceProvider();
        await using (var s = factoryProvider.CreateAsyncScope())
            Assert.Equal([5], await Collect(s.ServiceProvider.GetRequiredService<IZendiator>().StreamAsync(new GetOne(5))));
        Assert.Equal(1, factoryCalls);

        // Disposed scope: enumeration fails without leaking.
        var disposedServices = Services();
        await using var disposedProvider = disposedServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var disposedScope = disposedProvider.CreateAsyncScope();
        var disposedMediator = disposedScope.ServiceProvider.GetRequiredService<IZendiator>();
        var disposedStream = disposedMediator.StreamAsync(new GetNumbers(2));
        await disposedScope.DisposeAsync();
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await Collect(disposedStream));

    }
}
