using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests.Contracts;

public sealed class CanonicalStreamItemReferenceTests
{
    private sealed record Request;
    private sealed class Probe
    {
        public int[] Input = [-1, 1, 2];
        public Exception? SourceError, MapError, CleanupError;
        public int FirstResolves, MapResolves, LastResolves, MapEffects, ServiceDisposals;
        public readonly List<int> MapInputs = [], LastInputs = [];
        public readonly List<string> Trace = [];
        public readonly List<SourceCursor> Cursors = [];
        public readonly ContractEvents Events = new("Stream", 1);
    }

    private sealed class FirstFilter(Probe probe) : IStreamItemFilter<Request, int>, IDisposable
    {
        public bool Accept(Request request, int item, CancellationToken token)
        { probe.Trace.Add("F1:" + item); return item > 0; }
        public void Dispose() => probe.ServiceDisposals++;
    }

    private sealed class Mapper(Probe probe) : IStreamItemMap<Request, int>, IDisposable
    {
        public int Map(Request request, int item, CancellationToken token)
        {
            probe.MapInputs.Add(item); probe.MapEffects++; probe.Trace.Add("Map:" + item);
            if (probe.MapError is not null) { probe.Trace.Add("Map.throw"); throw probe.MapError; }
            return item * 2;
        }
        public void Dispose() => probe.ServiceDisposals++;
    }

    private sealed class LastFilter(Probe probe) : IStreamItemFilter<Request, int>, IDisposable
    {
        public bool Accept(Request request, int item, CancellationToken token)
        { probe.LastInputs.Add(item); probe.Trace.Add("F2:" + item); return item < 4; }
        public void Dispose() => probe.ServiceDisposals++;
    }

    private sealed class Source(Probe probe) : IAsyncEnumerable<int>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        { var cursor = new SourceCursor(probe); probe.Cursors.Add(cursor); return cursor; }
    }

    private sealed class SourceCursor(Probe probe) : IAsyncEnumerator<int>
    {
        private int _index = -1;
        public int Moves, Reads, Disposals;
        public int Current { get { Reads++; return probe.Input[_index]; } }
        public ValueTask<bool> MoveNextAsync()
        {
            Moves++;
            if (probe.SourceError is not null)
            { probe.Trace.Add("H.source-throw"); return ValueTask.FromException<bool>(probe.SourceError); }
            return new(++_index < probe.Input.Length);
        }
        public ValueTask DisposeAsync()
        {
            Disposals++; probe.Trace.Add("H.finally");
            if (probe.CleanupError is not null)
            { probe.Trace.Add("H.cleanup-throw"); return ValueTask.FromException(probe.CleanupError); }
            return default;
        }
    }

    private static ServiceProvider Provider(Probe probe)
    {
        var services = new ServiceCollection();
        services.AddTransient(_ => { probe.FirstResolves++; return new FirstFilter(probe); });
        services.AddTransient(_ => { probe.MapResolves++; return new Mapper(probe); });
        services.AddTransient(_ => { probe.LastResolves++; return new LastFilter(probe); });
        return services.BuildServiceProvider();
    }

    private static IAsyncEnumerable<int> Stream(Probe probe, ServiceProvider services, bool reverse = false)
    {
        IAsyncEnumerable<int> Start(Request request, CancellationToken token)
        { probe.Trace.Add("H.start"); return new Source(probe); }
        return reverse
            ? CanonicalStreamItemReference.FilterMapFilter<Request, int, LastFilter, Mapper, FirstFilter>(services, new(), Start, events: probe.Events)
            : CanonicalStreamItemReference.FilterMapFilter<Request, int, FirstFilter, Mapper, LastFilter>(services, new(), Start, events: probe.Events);
    }

    private static async Task<int[]> Collect(IAsyncEnumerable<int> stream)
    { var values = new List<int>(); await foreach (var item in stream) values.Add(item); return [.. values]; }

    [Fact]
    public async Task Filter_Map_Filter_uses_mapped_values_and_reversing_filters_changes_the_result()
    {
        var probe = new Probe(); using var services = Provider(probe);
        Assert.Equal(new[] { 2 }, await Collect(Stream(probe, services)));
        Assert.Equal(new[] { 2, 4 }, probe.LastInputs); Assert.Equal(new[] { 1, 2 }, probe.MapInputs);
        Assert.Equal(new[] { "H.start", "F1:-1", "F1:1", "Map:1", "F2:2", "F1:2", "Map:2", "F2:4", "H.finally" }, probe.Trace);
        var reversed = new Probe(); using var reversedServices = Provider(reversed);
        Assert.Equal(new[] { 2, 4 }, await Collect(Stream(reversed, reversedServices, reverse: true)));
    }

    [Fact]
    public async Task First_filter_rejection_never_resolves_or_calls_Map_and_last_filter()
    {
        var probe = new Probe { Input = [-2, -1] }; using var services = Provider(probe);
        Assert.Empty(await Collect(Stream(probe, services)));
        Assert.Equal(1, probe.FirstResolves); Assert.Equal(0, probe.MapResolves); Assert.Equal(0, probe.LastResolves);
        Assert.Empty(probe.MapInputs); Assert.Empty(probe.LastInputs); Assert.Equal(0, probe.MapEffects);
        Assert.Equal(1, Assert.Single(probe.Cursors).Disposals); Assert.Equal(0, probe.ServiceDisposals);
    }

    [Fact]
    public async Task Last_filter_rejection_preserves_Map_effect_and_early_break_does_not_prefetch()
    {
        var probe = new Probe { Input = [2, 1, 9] }; using var services = Provider(probe);
        await foreach (var item in Stream(probe, services)) { Assert.Equal(2, item); break; }
        Assert.Equal(new[] { 2, 1 }, probe.MapInputs); Assert.Equal(new[] { 4, 2 }, probe.LastInputs); Assert.Equal(2, probe.MapEffects);
        var cursor = Assert.Single(probe.Cursors); Assert.Equal(2, cursor.Moves); Assert.Equal(2, cursor.Reads); Assert.Equal(1, cursor.Disposals);
        Assert.Equal(new[] { "H.start", "F1:2", "Map:2", "F2:4", "F1:1", "Map:1", "F2:2", "H.finally" }, probe.Trace);
        Assert.Equal(0, probe.ServiceDisposals);
    }

    [Fact]
    public async Task Source_failure_keeps_identity_and_closes_before_any_hook_is_resolved()
    {
        var error = new InvalidOperationException("source"); var probe = new Probe { SourceError = error };
        using var services = Provider(probe); var cursor = Stream(probe, services).GetAsyncEnumerator();
        var observation = await ContractAwait.Observe(cursor.MoveNextAsync);
        Assert.Same(error, observation.Error); Assert.True(observation.Faulted); Assert.False(observation.Canceled); Assert.False(observation.CallsiteThrow);
        Assert.Equal(0, probe.FirstResolves); Assert.Equal(0, probe.MapResolves); Assert.Equal(0, probe.LastResolves);
        var source = Assert.Single(probe.Cursors); Assert.Equal(1, source.Moves); Assert.Equal(0, source.Reads); Assert.Equal(1, source.Disposals);
        Assert.Equal(new[] { "H.start", "H.source-throw", "H.finally" }, probe.Trace);
        await cursor.DisposeAsync(); Assert.Equal(1, source.Disposals);
    }

    [Fact]
    public async Task Map_callback_failure_keeps_identity_and_stops_before_last_filter_then_closes()
    {
        var error = new InvalidOperationException("map"); var probe = new Probe { Input = [1, 2], MapError = error };
        using var services = Provider(probe); var cursor = Stream(probe, services).GetAsyncEnumerator();
        var observation = await ContractAwait.Observe(cursor.MoveNextAsync);
        Assert.Same(error, observation.Error); Assert.True(observation.Faulted); Assert.False(observation.Canceled);
        Assert.Equal(1, probe.MapEffects); Assert.Equal(0, probe.LastResolves); Assert.Empty(probe.LastInputs);
        Assert.Equal(new[] { "H.start", "F1:1", "Map:1", "Map.throw", "H.finally" }, probe.Trace);
        var source = Assert.Single(probe.Cursors); Assert.Equal(1, source.Moves); Assert.Equal(1, source.Disposals);
        await cursor.DisposeAsync(); Assert.Equal(1, source.Disposals); Assert.Equal(0, probe.ServiceDisposals);
    }

    [Fact]
    public async Task Cleanup_failure_keeps_its_identity_and_is_not_retried_by_repeated_dispose()
    {
        var error = new ApplicationException("cleanup"); var probe = new Probe { Input = [1, 2], CleanupError = error };
        using var services = Provider(probe); var cursor = Stream(probe, services).GetAsyncEnumerator();
        Assert.True(await cursor.MoveNextAsync()); Assert.Equal(2, cursor.Current);
        var observation = await ContractAwait.Observe(cursor.DisposeAsync);
        Assert.Same(error, observation.Error); Assert.True(observation.Faulted); Assert.False(observation.Canceled);
        Assert.Equal(new[] { "H.start", "F1:1", "Map:1", "F2:2", "H.finally", "H.cleanup-throw" }, probe.Trace);
        await cursor.DisposeAsync(); Assert.Equal(1, Assert.Single(probe.Cursors).Disposals); Assert.Equal(0, probe.ServiceDisposals);
    }

    [Fact]
    public async Task Cleanup_failure_masks_callback_failure_with_normal_finally_priority()
    {
        var original = new InvalidOperationException("map"); var cleanup = new ApplicationException("cleanup");
        var probe = new Probe { Input = [1, 2], MapError = original, CleanupError = cleanup };
        using var services = Provider(probe); var cursor = Stream(probe, services).GetAsyncEnumerator();
        var observation = await ContractAwait.Observe(cursor.MoveNextAsync);
        Assert.Same(cleanup, observation.Error); Assert.True(observation.Faulted); Assert.False(observation.Canceled);
        Assert.Equal(new[] { "H.start", "F1:1", "Map:1", "Map.throw", "H.finally", "H.cleanup-throw" }, probe.Trace);
        Assert.Equal(1, probe.MapEffects); Assert.Equal(0, probe.LastResolves);
        await cursor.DisposeAsync(); Assert.Equal(1, Assert.Single(probe.Cursors).Disposals); Assert.Equal(0, probe.ServiceDisposals);
    }
}
