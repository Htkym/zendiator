using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests.Contracts;

public sealed class CanonicalStreamItemPendingTests
{
    private sealed record Request;
    private sealed class Probe
    {
        public int Resolves;
        public readonly List<string> Calls = [];
    }
    private sealed class First(Probe probe) : IStreamItemFilter<Request, int>
    {
        public bool Accept(Request request, int item, CancellationToken token)
        { probe.Calls.Add("F1:" + item); return item > 0; }
    }
    private sealed class Mapper(Probe probe) : IStreamItemMap<Request, int>
    {
        public int Map(Request request, int item, CancellationToken token)
        { probe.Calls.Add("Map:" + item); return item * 2; }
    }
    private sealed class Last(Probe probe) : IStreamItemFilter<Request, int>
    {
        public bool Accept(Request request, int item, CancellationToken token)
        { probe.Calls.Add("F2:" + item); return item < 4; }
    }
    private sealed class Source(ContractOnce<bool>? move = null, ContractOnce<bool>? dispose = null)
        : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        public int Moves, Reads, Disposals;
        public int Current { get { Reads++; return 1; } }
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) => this;
        public ValueTask<bool> MoveNextAsync()
        { Moves++; return Moves == 1 ? move?.Task ?? new(true) : new(false); }
        public ValueTask DisposeAsync() { Disposals++; return dispose?.Void ?? default; }
    }
    private static ServiceProvider Provider(Probe probe)
    {
        var services = new ServiceCollection();
        services.AddTransient(_ => { probe.Resolves++; return new First(probe); });
        services.AddTransient(_ => { probe.Resolves++; return new Mapper(probe); });
        services.AddTransient(_ => { probe.Resolves++; return new Last(probe); });
        return services.BuildServiceProvider();
    }
    private static IAsyncEnumerator<int> Cursor(ServiceProvider services, Source source)
        => CanonicalStreamItemReference.FilterMapFilter<Request, int, First, Mapper, Last>(services, new(), (_, _) => source).GetAsyncEnumerator();

    [Fact]
    public async Task Pending_source_Move_waits_before_Current_hooks_or_next_Move_and_consumes_once()
    {
        var once = new ContractOnce<bool>(); var source = new Source(move: once); var probe = new Probe();
        using var services = Provider(probe); var cursor = Cursor(services, source);
        var pending = cursor.MoveNextAsync();
        Assert.False(pending.IsCompleted); Assert.False(pending.IsFaulted); Assert.False(pending.IsCanceled);
        Assert.Equal(0, once.Consumptions); Assert.Equal(1, source.Moves); Assert.Equal(0, source.Reads);
        Assert.Equal(0, probe.Resolves); Assert.Empty(probe.Calls);
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
        Assert.Throws<InvalidOperationException>(() => cursor.MoveNextAsync());
        Assert.Throws<InvalidOperationException>(() => cursor.DisposeAsync());
        Assert.Equal(1, source.Moves); Assert.Equal(0, source.Disposals);
        var completion = pending.AsTask(); once.Complete(true);
        Assert.True(await completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, once.Consumptions); Assert.Equal(2, cursor.Current); Assert.Equal(1, source.Reads);
        Assert.Equal(new[] { "F1:1", "Map:1", "F2:2" }, probe.Calls);
        Assert.False(await cursor.MoveNextAsync()); await cursor.DisposeAsync();
        Assert.Equal(2, source.Moves); Assert.Equal(1, source.Disposals); Assert.Equal(1, once.Consumptions);
    }

    [Fact]
    public async Task Pending_source_Dispose_is_awaited_and_consumed_once_before_cursor_finishes()
    {
        var once = new ContractOnce<bool>(); var source = new Source(dispose: once); var probe = new Probe();
        using var services = Provider(probe); var cursor = Cursor(services, source);
        Assert.True(await cursor.MoveNextAsync()); Assert.Equal(2, cursor.Current);
        var pending = cursor.DisposeAsync();
        Assert.False(pending.IsCompleted); Assert.False(pending.IsFaulted); Assert.False(pending.IsCanceled);
        Assert.Equal(1, source.Disposals); Assert.Equal(0, once.Consumptions);
        Assert.Throws<InvalidOperationException>(() => cursor.Current);
        Assert.Throws<InvalidOperationException>(() => cursor.MoveNextAsync());
        Assert.Throws<InvalidOperationException>(() => cursor.DisposeAsync());
        Assert.Equal(1, source.Moves);
        var completion = pending.AsTask(); once.Complete(true);
        await completion.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(1, once.Consumptions);
        await cursor.DisposeAsync(); Assert.False(await cursor.MoveNextAsync());
        Assert.Equal(1, source.Disposals); Assert.Equal(1, source.Moves); Assert.Equal(1, once.Consumptions);
    }
}
