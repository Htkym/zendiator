using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string WrapperTraceProfile = "two-outer-saved-opens-drop-replace";
    private const string WrapperTraceFixtureCode = """
        public sealed class StreamOpaque(global::Zendiator.Generator.Tests.CanonicalStreamWrapperEventProbe p) : IStreamWrapper<Items,int> {
            public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(Items r,N n,CancellationToken c) where N : IStreamUpstream<Items,int> {
                p.Wrap(r,c); return Run(r,n,c);
            }
            private async System.Collections.Generic.IAsyncEnumerable<int> Run<N>(Items r,N n,
                [global::System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken c) where N : IStreamUpstream<Items,int> {
                p.WrapperBefore(r,c);
                try {
                    await Task.CompletedTask;
                    var changed=new Items(9);
                    var first=p.ObserveOpen(changed,n,p.FirstToken);
                    var second=p.ObserveOpen(changed,n,p.SecondToken);
                    await foreach(var value in first.ConfigureAwait(false)) {
                        p.WrapperDrop(r,c,value,first.Invocation); break;
                    }
                    await foreach(var value in second.ConfigureAwait(false)) {
                        p.WrapperItem(r,c,value,second.Invocation); yield return value;
                    }
                } finally { p.WrapperFinally(r,c); }
            }
        }
        """;
    private sealed record WrapperTraceOperation(string Kind, int Invocation, int Sequence, bool Completed,
        bool CompletedSuccessfully, bool Canceled, bool Faulted, bool? Value);
    private sealed record WrapperTraceResult(int[][] Values, StreamSeedEvent[] Events, WrapperTraceOperation[] Operations,
        int Starts, int Moves, int Reads, int Disposals);

    [Fact]
    public Task Stream_wrapper_common_events_saved_opens_drop_and_outer_replay_match_reference()
        => RunWrapperTrace(Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_C08_WRAPPER_CASE"));
    internal static Task ReplayStreamWrapperTraceCase(string id)
    { ArgumentException.ThrowIfNullOrEmpty(id); return RunWrapperTrace(id); }

    private static async Task RunWrapperTrace(string? id)
    {
        if (!string.IsNullOrEmpty(id) && id != WrapperTraceProfile) throw new ArgumentException("Choose the single fixed wrapper profile.", nameof(id));
        WriteWrapperTraceArtifact("profile.json", new
        {
            id = WrapperTraceProfile,
            cases = 1,
            input = new[] { 1, 2 },
            outerEnumerationsPerSeries = 2,
            opensPerOuter = 2,
            series = new[] { "reference", "canonical" },
            savedBeforeEnumerating = true,
            sameReplacementRequestWithinOuter = true,
            distinctReplacementTokens = true,
            candidateOnly = true,
            productionEnabled = false,
            performanceMeasurements = 0
        });
        var f = FallbackFixtureFor("wrapper-trace"); Assert.Null(f.Fused);
        var reference = await RunWrapperTraceBranch(f, "reference");
        var canonical = await RunWrapperTraceBranch(f, "canonical");
        WriteWrapperTraceArtifact("comparison.json", new
        {
            profile = WrapperTraceProfile,
            reference,
            canonical,
            candidateOnly = true,
            productionEnabled = false,
            fusedAcrossWrapper = false
        });
        Assert.Equal(reference.Values.Length, canonical.Values.Length);
        for (var i = 0; i < reference.Values.Length; i++) Assert.Equal(reference.Values[i], canonical.Values[i]);
        Assert.Equal(reference.Events, canonical.Events); Assert.Equal(reference.Operations, canonical.Operations);
        Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events, Operations = canonical.Operations }, canonical);
        CheckWrapperTrace(reference); CheckWrapperTrace(canonical);
    }
    private static void WriteWrapperTraceArtifact(string name, object data)
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(evidence)) return;
        var path = Path.Combine(evidence, "stream-wrapper-trace", name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }
    private static async Task<WrapperTraceResult> RunWrapperTraceBranch(FallbackFixture f, string backend)
    {
        using var api = new CancellationTokenSource(); using var firstToken = new CancellationTokenSource(); using var secondToken = new CancellationTokenSource();
        var p = new CanonicalStreamWrapperEventProbe { ApiToken = api.Token, FirstToken = firstToken.Token, SecondToken = secondToken.Token };
        var request = Activator.CreateInstance(f.Request, [1])!;
        var services = new ServiceCollection(); var roles = new[] { "H", "F1", "W", "F2" };
        for (var i = 0; i < f.Services.Length; i++)
        { var type = f.Services[i]; var role = roles[i]; services.AddTransient(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, p)!; }); }
        using var provider = services.BuildServiceProvider();
        IAsyncEnumerable<int> stream;
        if (backend == "reference")
        {
            var bridge = Activator.CreateInstance(f.Bridge, provider)!;
            var start = f.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(f.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<int>)), bridge);
            stream = (IAsyncEnumerable<int>)typeof(CanonicalStreamWrapperReference).GetMethod("FilterWrapperFilter")!
                .MakeGenericMethod(f.Request, typeof(int), f.Services[1], f.Services[2], f.Services[3])
                .Invoke(null, [provider, request, start, api.Token, null])!;
        }
        else stream = (IAsyncEnumerable<int>)f.Canonical.Invoke(null, [provider, request, api.Token])!;
        var observed = p.ObserveOuter(stream, request, api.Token); var values = new List<int[]>(); var operations = new List<WrapperTraceOperation>();
        var complete = false;
        try
        {
            Assert.Empty(p.Snapshot());
            for (var enumeration = 0; enumeration < 2; enumeration++)
            {
                var cursor = observed.GetAsyncEnumerator(); var outer = p.LastOuterInvocation; var output = new List<int>(); var ended = false;
                for (var sequence = 0; sequence < 3; sequence++)
                {
                    var move = cursor.MoveNextAsync();
                    var operation = new WrapperTraceOperation("Move", outer, sequence, move.IsCompleted, move.IsCompletedSuccessfully, move.IsCanceled, move.IsFaulted, null);
                    operations.Add(operation); Assert.True(operation.Completed);
                    var ok = await move; operations[^1] = operation with { Value = ok };
                    if (!ok) { ended = true; break; }
                    var reads = p.Reads; var item = cursor.Current; _ = cursor.Current; Assert.Equal(reads, p.Reads);
                    output.Add(item); p.ConsumerYield(outer, item);
                }
                Assert.True(ended, "Two outputs plus termination are the finite wrapper profile.");
                var dispose = cursor.DisposeAsync();
                operations.Add(new("Dispose", outer, 0, dispose.IsCompleted, dispose.IsCompletedSuccessfully, dispose.IsCanceled, dispose.IsFaulted, null));
                Assert.True(dispose.IsCompleted); await dispose;
                values.Add([.. output]);
            }
            complete = true;
            return new([.. values], p.Snapshot(), [.. operations], p.Starts, p.Moves, p.Reads, p.Disposals);
        }
        finally
        {
            WriteWrapperTraceArtifact(backend + ".json", new
            {
                profile = WrapperTraceProfile,
                backend,
                complete,
                values,
                events = p.Snapshot(),
                operations,
                p.Starts,
                p.Moves,
                p.Reads,
                p.Disposals
            });
        }
    }
    private static void CheckWrapperTrace(WrapperTraceResult result)
    {
        Assert.Equal(2, result.Values.Length); Assert.All(result.Values, values => Assert.Equal(new[] { 1, 2 }, values));
        Assert.Equal(4, result.Starts); Assert.Equal(8, result.Moves); Assert.Equal(6, result.Reads); Assert.Equal(4, result.Disposals);
        Assert.All(result.Operations, o => { Assert.True(o.Completed); Assert.True(o.CompletedSuccessfully); Assert.False(o.Faulted); Assert.False(o.Canceled); });
        var events = result.Events;
        Assert.Equal(new[] { 1, 4 }, events.Where(e => e.Kind == "EnumeratorAcquire" && e.Stage == "outer").Select(e => e.Invocation));
        Assert.Equal(new[] { 2, 3, 5, 6 }, events.Where(e => e.Kind == "UpstreamOpen").Select(e => e.Invocation));
        Assert.DoesNotContain(events, e => e.Kind is "Map" or "Throw" or "InnerDisposeThrow");
        for (var run = 0; run < 2; run++)
        {
            var outer = run == 0 ? 1 : 4; var first = outer + 1; var second = outer + 2; var replacementRequest = run + 2;
            var local = events.Where(e => e.Invocation == outer).ToArray(); var children = events.Where(e => e.ParentInvocation == outer).ToArray();
            Assert.All(local, e => { Assert.Equal(1, e.Request); Assert.Equal(1, e.Token); Assert.Equal(0, e.ParentInvocation); });
            Assert.All(children, e => { Assert.Equal(replacementRequest, e.Request); Assert.Equal(e.Invocation == first ? 2 : 3, e.Token); });
            Assert.DoesNotContain(local, e => e.Stage is "H" or "F1"); Assert.DoesNotContain(children, e => e.Stage == "F2");
            Assert.Single(local, e => e.Kind == "WrapperWrap"); Assert.Single(local, e => e.Kind == "WrapperBefore"); Assert.Single(local, e => e.Kind == "WrapperFinally");
            var opens = events.Where(e => e.Kind == "UpstreamOpen" && e.ParentInvocation == outer).ToArray(); Assert.Equal(2, opens.Length);
            Assert.True(Array.IndexOf(events, opens[1]) < Array.FindIndex(events, e => e.Kind == "HandlerStart" && e.Invocation == first));
            var drop = Assert.Single(local, e => e.Kind == "WrapperDrop"); Assert.Equal(first, drop.RelatedInvocation); Assert.Equal(0, drop.RelatedSequence); Assert.Equal(1, drop.Input);
            var droppedFilter = Assert.Single(children, e => e.Invocation == first && e.Kind == "Filter" && e.Stage == "F1");
            Assert.True(droppedFilter.Result!.Value); Assert.True(Array.IndexOf(events, droppedFilter) < Array.IndexOf(events, drop));
            Assert.Equal(new[] { 1, 2 }, local.Where(e => e.Kind == "WrapperItem").Select(e => e.Output!.Value));
            Assert.All(local.Where(e => e.Kind == "WrapperItem"), e => Assert.Equal(second, e.RelatedInvocation));
            Assert.Equal(new[] { 0, 1 }, local.Where(e => e.Kind == "WrapperItem").Select(e => e.RelatedSequence!.Value));
            Assert.Equal(2, local.Count(e => e.Kind == "Filter" && e.Stage == "F2")); Assert.Equal(2, local.Count(e => e.Kind == "Yield"));
            Assert.All(local.Where(e => e.Kind == "Filter" || e.Kind == "Yield"), e => Assert.Equal(second, e.RelatedInvocation));
            foreach (var item in local.Where(e => e.Kind == "WrapperItem"))
            {
                var last = Assert.Single(local, e => e.Kind == "Filter" && e.Stage == "F2" && e.Sequence == item.Sequence);
                var yielded = Assert.Single(local, e => e.Kind == "Yield" && e.Sequence == item.Sequence);
                Assert.Equal(item.Output, last.Input); Assert.Equal(item.Output, yielded.Output);
                Assert.True(Array.IndexOf(events, item) < Array.IndexOf(events, last) && Array.IndexOf(events, last) < Array.IndexOf(events, yielded));
            }
            Assert.True(Array.IndexOf(events, drop) < Array.FindIndex(events, e => e.Invocation == first && e.Kind == "InnerDisposeStart"));
            for (var child = first; child <= second; child++)
            {
                var source = children.Where(e => e.Invocation == child).ToArray();
                Assert.Single(source, e => e.Kind == "EnumeratorAcquire"); Assert.Single(source, e => e.Kind == "InnerGet");
                Assert.Single(source, e => e.Kind == "HandlerStart"); Assert.Single(source, e => e.Kind == "InnerDisposeStart"); Assert.Single(source, e => e.Kind == "InnerDisposeEnd");
                Assert.Single(source, e => e.Kind == "EnumeratorDispose"); Assert.Single(source, e => e.Kind == "EnumeratorDisposed");
                Assert.Single(source, e => e.Kind == "Resolve" && e.Stage == "F1");
                Assert.Equal(child == first ? 1 : 2, source.Count(e => e.Kind == "InnerCurrent"));
                Assert.Equal(child == first ? 1 : 3, source.Count(e => e.Kind == "InnerMoveNext"));
                Assert.All(source.Where(e => e.Kind == "EnumeratorAcquire"), e => Assert.Equal(0, e.EnumeratorToken));
                Assert.True(Array.FindIndex(events, e => e.Invocation == child && e.Kind == "InnerDisposeEnd") < Array.FindIndex(events, e => e.Invocation == outer && e.Kind == "WrapperFinally"));
            }
            Assert.Single(local, e => e.Kind == "Resolve" && e.Stage == "F2");
            Assert.All(local.Where(e => e.Kind == "EnumeratorAcquire"), e => Assert.Equal(0, e.EnumeratorToken));
        }
    }
}
