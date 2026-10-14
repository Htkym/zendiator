using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests.Contracts;

public sealed class CanonicalStreamFlowReferenceTests
{
    private sealed record Request(int Value);
    private sealed class Probe
    {
        public readonly List<string> Trace = [];
        public int FilterResolves, MapResolves, ServiceDisposals;
        public CancellationTokenSource? Cancel;
        public bool Context;
    }
    private sealed class Filter(Probe probe) : IStreamItemFilter<Request, int>, IDisposable
    {
        public bool Accept(Request request, int item, CancellationToken token)
        {
            probe.Trace.Add("filter:" + item);
            if (probe.Context) ContractEvents.Ambient.Value = "filter-callback";
            if (probe.Cancel is not null) { probe.Cancel.Cancel(); return false; }
            return item > 0;
        }
        public void Dispose() => probe.ServiceDisposals++;
    }
    private sealed class Mapper(Probe probe) : IStreamItemMap<Request, int>, IDisposable
    {
        public int Map(Request request, int item, CancellationToken token)
        {
            probe.Trace.Add("map:" + item);
            if (probe.Context) Assert.Equal("filter-callback", ContractEvents.Ambient.Value);
            return item * 2;
        }
        public void Dispose() => probe.ServiceDisposals++;
    }
    private sealed class Source(params int[] values) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index = -1;
        public int Moves, Reads, Disposals, Acquisitions;
        public int Current { get { Reads++; return values[_index]; } }
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) { Acquisitions++; return this; }
        public ValueTask<bool> MoveNextAsync() { Moves++; return new(++_index < values.Length); }
        public ValueTask DisposeAsync() { Disposals++; return default; }
    }
    private static ServiceProvider Provider(Probe probe)
    {
        var services = new ServiceCollection(); services.AddSingleton(probe);
        services.AddTransient(_ => { probe.FilterResolves++; if (probe.Context) ContractEvents.Ambient.Value = "filter-factory"; return new Filter(probe); });
        services.AddTransient(_ => { probe.MapResolves++; if (probe.Context) Assert.Equal("filter-callback", ContractEvents.Ambient.Value); return new Mapper(probe); });
        return services.BuildServiceProvider();
    }
    [Fact]
    public async Task Declared_filter_then_map_is_lazy_and_reads_Current_once_per_input()
    {
        var probe = new Probe(); using var provider = Provider(probe); var source = new Source(-1, 1, 2); var starts = 0;
        var events = new ContractEvents("Stream", 1);
        var stream = CanonicalStreamFlowReference.FilterMap<Request, int, Filter, Mapper>(provider, new(1), (_, _) => { starts++; return source; }, events: events);
        var cursor = stream.GetAsyncEnumerator(); Assert.Equal(0, starts); Assert.Equal(0, source.Acquisitions);
        Assert.True(await cursor.MoveNextAsync()); Assert.Equal(2, cursor.Current); Assert.Equal(2, cursor.Current);
        Assert.True(await cursor.MoveNextAsync()); Assert.Equal(4, cursor.Current); Assert.False(await cursor.MoveNextAsync());
        await cursor.DisposeAsync(); Assert.Equal(1, starts); Assert.Equal(3, source.Reads); Assert.Equal(1, source.Disposals);
        Assert.Equal(1, probe.FilterResolves); Assert.Equal(1, probe.MapResolves);
        Assert.Equal(new[] { "filter:-1", "filter:1", "map:1", "filter:2", "map:2" }, probe.Trace);
        Assert.Single(events.Snapshot(), e => e.Stage == "filter" && e.Kind == "resolve");
        Assert.Single(events.Snapshot(), e => e.Stage == "map" && e.Kind == "resolve");
        Assert.Equal(0, probe.ServiceDisposals);
    }
    [Fact]
    public async Task Dropped_items_never_resolve_the_downstream_map()
    {
        var probe = new Probe(); using var provider = Provider(probe); var source = new Source(-2, -1);
        await foreach (var _ in CanonicalStreamFlowReference.FilterMap<Request, int, Filter, Mapper>(provider, new(1), (_, _) => source)) Assert.Fail("No item should pass.");
        Assert.Equal(0, probe.MapResolves); Assert.Equal(1, probe.FilterResolves); Assert.Equal(2, source.Reads); Assert.Equal(1, source.Disposals);
    }
    [Fact]
    public async Task Cancel_and_false_stops_before_next_source_move_and_cleans_once()
    {
        using var cancel = new CancellationTokenSource(); var probe = new Probe { Cancel = cancel }; using var provider = Provider(probe); var source = new Source(1, 2);
        var cursor = CanonicalStreamFlowReference.FilterMap<Request, int, Filter, Mapper>(provider, new(1), (_, _) => source, cancel.Token).GetAsyncEnumerator();
        var move = cursor.MoveNextAsync(); var canceled = move.IsCanceled;
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await move);
        Assert.Equal(cancel.Token, error.CancellationToken); Assert.True(canceled); Assert.Equal(1, source.Moves);
        Assert.Equal(0, probe.MapResolves); Assert.Equal(1, source.Disposals); await cursor.DisposeAsync(); Assert.Equal(1, source.Disposals);
    }
    [Fact]
    public async Task One_move_context_keeps_factory_and_callbacks_together_and_early_dispose_borrows_services()
    {
        var before = ContractEvents.Ambient.Value; ContractEvents.Ambient.Value = "caller";
        try
        {
            var probe = new Probe { Context = true }; using var provider = Provider(probe); var source = new Source(1, 2);
            var cursor = CanonicalStreamFlowReference.FilterMap<Request, int, Filter, Mapper>(provider, new(1), (_, _) => source).GetAsyncEnumerator();
            Assert.True(await cursor.MoveNextAsync()); Assert.Equal("caller", ContractEvents.Ambient.Value);
            await cursor.DisposeAsync(); await cursor.DisposeAsync(); Assert.Equal(1, source.Disposals); Assert.Equal(0, probe.ServiceDisposals);
            provider.Dispose(); Assert.Equal(2, probe.ServiceDisposals);
        }
        finally { ContractEvents.Ambient.Value = before; }
    }
}
