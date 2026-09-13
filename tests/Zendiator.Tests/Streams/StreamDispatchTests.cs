using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class StreamDispatchTests
{
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
        Assert.Equal(16, (await Collect(mediator.StreamAsync(new GetNumbers(16)))).Count);
        var items1024 = await Collect(mediator.StreamAsync(new GetNumbers(1024)));
        Assert.Equal(1024, items1024.Count);
        for (var i = 0; i < 1024; i++) Assert.Equal(i, items1024[i]);
    }

    [Fact]
    public async Task S_handler_runs_once_per_enumeration()
    {
        GetNumbersHandler.Calls = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal([0, 1], await Collect(mediator.StreamAsync(new GetNumbers(2))));
        Assert.Equal(1, GetNumbersHandler.Calls);
        Assert.Equal([0, 1], await Collect(mediator.StreamAsync(new GetNumbers(2))));
        Assert.Equal(2, GetNumbersHandler.Calls);
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
        // Unenumerated stream has no side effects.
        GetLazyHandler.Started = 0;
        _ = mediator.StreamAsync(new GetLazy(5));
        Assert.Equal(0, GetLazyHandler.Started);
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
    public async Task E_exceptions_and_D_disposal()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        // First-item exception.
        Assert.Same(GetFailHandler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await Collect(mediator.StreamAsync(new GetFail(3, 0)))));
        // Mid-stream exception stops later items.
        var seen = new List<int>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var i in mediator.StreamAsync(new GetFail(5, 2))) seen.Add(i);
        });
        Assert.Equal([0, 1], seen);
        // Break after first.
        var first = new List<int>();
        await foreach (var i in mediator.StreamAsync(new GetNumbers(10)))
        {
            first.Add(i);
            break;
        }
        Assert.Equal([0], first);
        // Break after N.
        var n = new List<int>();
        await foreach (var i in mediator.StreamAsync(new GetNumbers(10)))
        {
            n.Add(i);
            if (n.Count == 3) break;
        }
        Assert.Equal([0, 1, 2], n);
        // Explicit DisposeAsync.
        var e = mediator.StreamAsync(new GetNumbers(10)).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        await e.DisposeAsync();
    }

    [Fact]
    public async Task T_scoped_singleton_transient_and_isolation()
    {
        var services = Services();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        int firstHash;
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            var items = await Collect(mediator.StreamAsync(new GetScopedItems(2)));
            Assert.Equal([0, 1], items);
            firstHash = scope.ServiceProvider.GetRequiredService<Trace>().GetHashCode();
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            await Collect(mediator.StreamAsync(new GetScopedItems(1)));
            Assert.NotEqual(firstHash, scope.ServiceProvider.GetRequiredService<Trace>().GetHashCode());
        }
        // Concurrent streams in separate scopes stay isolated.
        await using var s1 = provider.CreateAsyncScope();
        await using var s2 = provider.CreateAsyncScope();
        var m1 = s1.ServiceProvider.GetRequiredService<IZendiator>();
        var m2 = s2.ServiceProvider.GetRequiredService<IZendiator>();
        var t1 = Collect(m1.StreamAsync(new GetNumbers(4)));
        var t2 = Collect(m2.StreamAsync(new GetNumbers(4)));
        var r = await Task.WhenAll(t1, t2);
        Assert.Equal([0, 1, 2, 3], r[0]);
        Assert.Equal([0, 1, 2, 3], r[1]);
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
    public async Task T_singleton_transient_factory_and_disposed_scope()
    {
        // Singleton: same handler across scopes (via container reuse, per-enumeration resolve still returns same singleton).
        var singletonServices = new ServiceCollection();
        singletonServices.AddScoped<Trace>();
        singletonServices.AddScoped<AuditLog>();
        singletonServices.AddScoped<Gate>();
        singletonServices.AddZendiator(ServiceLifetime.Singleton);
        await using var singletonProvider = singletonServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        GetNumbersHandler.Calls = 0;
        await using (var s = singletonProvider.CreateAsyncScope())
            await Collect(s.ServiceProvider.GetRequiredService<IZendiator>().StreamAsync(new GetNumbers(1)));
        await using (var s = singletonProvider.CreateAsyncScope())
            await Collect(s.ServiceProvider.GetRequiredService<IZendiator>().StreamAsync(new GetNumbers(1)));
        Assert.Equal(2, GetNumbersHandler.Calls);

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
        Assert.True(factoryCalls >= 1);

        // Disposed scope: enumeration fails without leaking.
        var disposedServices = Services();
        await using var disposedProvider = disposedServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var disposedScope = disposedProvider.CreateAsyncScope();
        var disposedMediator = disposedScope.ServiceProvider.GetRequiredService<IZendiator>();
        var disposedStream = disposedMediator.StreamAsync(new GetNumbers(2));
        await disposedScope.DisposeAsync();
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(async () => await Collect(disposedStream));

        // Multiple providers stay isolated.
        await using var p1 = Services().BuildServiceProvider();
        await using var p2 = Services().BuildServiceProvider();
        await using var s1 = p1.CreateAsyncScope();
        await using var s2 = p2.CreateAsyncScope();
        Assert.Equal([0, 1], await Collect(s1.ServiceProvider.GetRequiredService<IZendiator>().StreamAsync(new GetNumbers(2))));
        Assert.Equal([0, 1], await Collect(s2.ServiceProvider.GetRequiredService<IZendiator>().StreamAsync(new GetNumbers(2))));
    }

    [Fact]
    public async Task D_no_double_dispose_and_handler_finally()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var e = mediator.StreamAsync(new GetNumbers(5)).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        await e.DisposeAsync();
        await e.DisposeAsync();
        Assert.False(await e.MoveNextAsync());
    }
}
