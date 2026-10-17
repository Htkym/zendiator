using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests.Contracts;

public sealed class CanonicalStreamWrapperReferenceTests
{
    private sealed record Request(int Value);
    private sealed record Visit(Request Request, int Item, CancellationToken Token);
    private enum Mode { Transform, Empty, Alternative, Drop, TwoOpen, Owned, Direct }
    private sealed class Probe
    {
        public Request Outer = new(1);
        public int[] Input = [1, 2, 3];
        public Mode Mode;
        public bool RejectTwenty;
        public int WrapperResolves, AfterResolves, ServiceDisposals;
        public readonly List<string> Trace = [];
        public readonly List<Visit> Starts = [], BeforeVisits = [], AfterVisits = [], WrapVisits = [];
        public readonly List<BeforeFilter> BeforeInstances = [];
        public readonly List<SourceCursor> Cursors = [];
        public readonly List<(Request Request, CancellationToken Token)> Replacements = [];
        public readonly ContractEvents Events = new("Stream", 1);
    }

    private sealed class BeforeFilter(Probe probe) : IStreamItemFilter<Request, int>, IDisposable
    {
        public int Calls;
        public bool Accept(Request request, int item, CancellationToken token)
        {
            Calls++; probe.BeforeVisits.Add(new(request, item, token)); probe.Trace.Add("F1:" + item);
            return item > 0;
        }
        public void Dispose() => probe.ServiceDisposals++;
    }

    private sealed class AfterFilter(Probe probe) : IStreamItemFilter<Request, int>, IDisposable
    {
        public bool Accept(Request request, int item, CancellationToken token)
        {
            probe.AfterVisits.Add(new(request, item, token)); probe.Trace.Add("F2:" + item);
            return !probe.RejectTwenty || item != 20;
        }
        public void Dispose() => probe.ServiceDisposals++;
    }

    private sealed class Wrapper(Probe probe) : IStreamWrapper<Request, int>, IDisposable
    {
        public IAsyncEnumerable<int> Wrap<TUpstream>(Request request, TUpstream upstream, CancellationToken token)
            where TUpstream : IStreamUpstream<Request, int>
        {
            probe.WrapVisits.Add(new(request, 0, token));
            if (probe.Mode == Mode.Direct)
            {
                probe.Trace.Add("W.direct");
                var replacement = probe.Replacements[0];
                return upstream.Open(replacement.Request, replacement.Token);
            }
            return Run(request, upstream, token);
        }

        private async IAsyncEnumerable<int> Run<TUpstream>(Request request, TUpstream upstream,
            [EnumeratorCancellation] CancellationToken token)
            where TUpstream : IStreamUpstream<Request, int>
        {
            probe.Trace.Add("W.before");
            try
            {
                if (probe.Mode == Mode.Empty) yield break;
                if (probe.Mode == Mode.Alternative) { yield return 100; yield break; }
                if (probe.Mode == Mode.TwoOpen)
                {
                    foreach (var replacement in probe.Replacements)
                        await foreach (var item in upstream.Open(replacement.Request, replacement.Token)
                            .WithCancellation(replacement.Token).ConfigureAwait(false))
                        { probe.Trace.Add("W:" + item); yield return item; }
                    yield break;
                }
                await foreach (var item in upstream.Open(request, token).WithCancellation(token).ConfigureAwait(false))
                {
                    probe.Trace.Add("W:" + item);
                    if (probe.Mode == Mode.Drop && item == 2) continue;
                    yield return probe.Mode == Mode.Transform ? item * 10 : item;
                }
            }
            finally { probe.Trace.Add("W.finally"); }
        }

        public void Dispose() => probe.ServiceDisposals++;
    }

    private sealed class Source(Probe probe) : IAsyncEnumerable<int>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        {
            var cursor = new SourceCursor(probe, token); probe.Cursors.Add(cursor); return cursor;
        }
    }

    private sealed class SourceCursor(Probe probe, CancellationToken token) : IAsyncEnumerator<int>
    {
        private int _index = -1;
        public readonly CancellationToken Token = token;
        public int Moves, Reads, Disposals;
        public int Current { get { Reads++; return probe.Input[_index]; } }
        public ValueTask<bool> MoveNextAsync() { Moves++; return new(++_index < probe.Input.Length); }
        public ValueTask DisposeAsync() { Disposals++; probe.Trace.Add("H.finally"); return default; }
    }

    private static ServiceProvider Provider(Probe probe)
    {
        var services = new ServiceCollection();
        services.AddTransient(_ => { var filter = new BeforeFilter(probe); probe.BeforeInstances.Add(filter); return filter; });
        services.AddTransient(_ => { probe.WrapperResolves++; return new Wrapper(probe); });
        services.AddTransient(_ => { probe.AfterResolves++; return new AfterFilter(probe); });
        return services.BuildServiceProvider();
    }

    private static IAsyncEnumerable<int> Stream(Probe probe, ServiceProvider services, CancellationToken token = default)
        => CanonicalStreamWrapperReference.FilterWrapperFilter<Request, int, BeforeFilter, Wrapper, AfterFilter>(
            services, probe.Outer, (request, selected) =>
            {
                probe.Starts.Add(new(request, 0, selected)); probe.Trace.Add("H.start"); return new Source(probe);
            }, token, probe.Events);

    private static async Task<int[]> Collect(IAsyncEnumerable<int> stream)
    { var output = new List<int>(); await foreach (var item in stream) output.Add(item); return [.. output]; }

    [Fact]
    public async Task Declared_F1_W_F2_order_preserves_wrapper_work_when_F2_rejects()
    {
        var probe = new Probe { Mode = Mode.Transform, Input = [0, 1, 2, 3], RejectTwenty = true };
        using var services = Provider(probe);
        var stream = Stream(probe, services); Assert.Empty(probe.Trace); Assert.Equal(0, probe.WrapperResolves);
        Assert.Equal(new[] { 10, 30 }, await Collect(stream));
        Assert.Equal(new[] { "W.before", "H.start", "F1:0", "F1:1", "W:1", "F2:10", "F1:2", "W:2", "F2:20",
            "F1:3", "W:3", "F2:30", "H.finally", "W.finally" }, probe.Trace);
        Assert.Single(probe.BeforeInstances); Assert.Equal(1, probe.AfterResolves); Assert.Equal(1, probe.WrapperResolves);
        Assert.Equal(4, Assert.Single(probe.Cursors).Reads); Assert.Equal(0, probe.ServiceDisposals);
    }

    [Fact]
    public async Task Zero_Open_empty_output_never_starts_handler_or_either_hook()
    {
        var probe = new Probe { Mode = Mode.Empty }; using var services = Provider(probe);
        Assert.Empty(await Collect(Stream(probe, services)));
        Assert.Empty(probe.Starts); Assert.Empty(probe.BeforeInstances); Assert.Equal(0, probe.AfterResolves);
        Assert.DoesNotContain(probe.Events.Snapshot(), e => e.Stage == "upstream" && e.Kind == "open");
        Assert.Equal(new[] { "W.before", "W.finally" }, probe.Trace); Assert.Equal(0, probe.ServiceDisposals);
    }

    [Fact]
    public async Task Zero_Open_alternative_output_reaches_only_outer_F2()
    {
        using var token = new CancellationTokenSource(); var probe = new Probe { Mode = Mode.Alternative };
        using var services = Provider(probe);
        Assert.Equal(new[] { 100 }, await Collect(Stream(probe, services, token.Token)));
        Assert.Empty(probe.Starts); Assert.Empty(probe.BeforeInstances); Assert.Equal(1, probe.AfterResolves);
        Assert.DoesNotContain(probe.Events.Snapshot(), e => e.Stage == "upstream" && e.Kind == "open");
        var visit = Assert.Single(probe.AfterVisits); Assert.Same(probe.Outer, visit.Request); Assert.Equal(token.Token, visit.Token);
        Assert.Equal(new[] { "W.before", "F2:100", "W.finally" }, probe.Trace);
    }

    [Fact]
    public async Task Wrapper_dropped_item_keeps_F1_effect_but_never_reaches_F2()
    {
        var probe = new Probe { Mode = Mode.Drop }; using var services = Provider(probe);
        Assert.Equal(new[] { 1, 3 }, await Collect(Stream(probe, services)));
        Assert.Equal(new[] { 1, 2, 3 }, probe.BeforeVisits.Select(v => v.Item));
        Assert.Equal(new[] { 1, 3 }, probe.AfterVisits.Select(v => v.Item));
        Assert.Contains("W:2", probe.Trace); Assert.DoesNotContain("F2:2", probe.Trace);
        Assert.Equal(1, Assert.Single(probe.Cursors).Disposals);
    }

    [Fact]
    public async Task Two_Open_calls_have_independent_cursor_hook_state_and_replaced_upstream_inputs()
    {
        using var outer = new CancellationTokenSource(); using var first = new CancellationTokenSource(); using var second = new CancellationTokenSource();
        var probe = new Probe { Mode = Mode.TwoOpen, Input = [1, 2] };
        probe.Replacements.Add((new(11), first.Token)); probe.Replacements.Add((new(22), second.Token));
        using var services = Provider(probe);
        Assert.Equal(new[] { 1, 2, 1, 2 }, await Collect(Stream(probe, services, outer.Token)));
        Assert.Equal(2, probe.Events.Snapshot().Count(e => e.Stage == "upstream" && e.Kind == "open"));
        Action<SourceCursor> completed = c => { Assert.Equal(3, c.Moves); Assert.Equal(2, c.Reads); Assert.Equal(1, c.Disposals); };
        Assert.Collection(probe.Cursors, completed, completed); Assert.NotSame(probe.Cursors[0], probe.Cursors[1]);
        Assert.Collection(probe.BeforeInstances, f => Assert.Equal(2, f.Calls), f => Assert.Equal(2, f.Calls));
        Assert.NotSame(probe.BeforeInstances[0], probe.BeforeInstances[1]);
        Assert.Equal(1, probe.WrapperResolves); Assert.Equal(1, probe.AfterResolves);
        for (var i = 0; i < 2; i++)
        {
            var replacement = probe.Replacements[i]; var start = probe.Starts[i];
            Assert.Same(replacement.Request, start.Request); Assert.Equal(replacement.Token, start.Token); Assert.Equal(replacement.Token, probe.Cursors[i].Token);
            Assert.All(probe.BeforeVisits.Skip(i * 2).Take(2), v => { Assert.Same(replacement.Request, v.Request); Assert.Equal(replacement.Token, v.Token); });
        }
        Assert.All(probe.AfterVisits, v => { Assert.Same(probe.Outer, v.Request); Assert.Equal(outer.Token, v.Token); });
        var wrap = Assert.Single(probe.WrapVisits); Assert.Same(probe.Outer, wrap.Request); Assert.Equal(outer.Token, wrap.Token);
    }

    [Fact]
    public async Task Early_break_closes_wrapper_owned_or_directly_delegated_cursor_once_without_prefetch()
    {
        foreach (var mode in new[] { Mode.Owned, Mode.Direct })
        {
            using var outer = new CancellationTokenSource(); using var replacement = new CancellationTokenSource();
            var probe = new Probe { Mode = mode }; probe.Replacements.Add((new(9), replacement.Token));
            using var services = Provider(probe);
            await foreach (var item in Stream(probe, services, outer.Token).WithCancellation(outer.Token))
            { Assert.Equal(1, item); break; }
            var cursor = Assert.Single(probe.Cursors); Assert.Equal(1, cursor.Moves); Assert.Equal(1, cursor.Reads); Assert.Equal(1, cursor.Disposals);
            Assert.Single(probe.BeforeVisits); Assert.Single(probe.AfterVisits); Assert.Equal(0, probe.ServiceDisposals);
            Assert.All(probe.AfterVisits, v => { Assert.Same(probe.Outer, v.Request); Assert.Equal(outer.Token, v.Token); });
            if (mode == Mode.Owned)
                Assert.Equal(new[] { "H.finally", "W.finally" }, probe.Trace.TakeLast(2));
            else
            {
                Assert.Equal("H.finally", probe.Trace[^1]); Assert.DoesNotContain("W.finally", probe.Trace);
                var start = Assert.Single(probe.Starts); Assert.Same(probe.Replacements[0].Request, start.Request);
                Assert.Equal(replacement.Token, start.Token); Assert.Equal(replacement.Token, cursor.Token);
            }
        }
    }
}
