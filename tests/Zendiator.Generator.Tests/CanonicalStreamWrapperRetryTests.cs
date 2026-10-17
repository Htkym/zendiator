using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string WrapperRetryProfile = "first-source-fault-close-then-reopen";
    private const string WrapperRetryFixtureCode = """
        public sealed class StreamOpaque(global::Zendiator.Generator.Tests.CanonicalStreamWrapperEventProbe p) : IStreamWrapper<Items,int> {
            public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(Items r,N n,CancellationToken c) where N : IStreamUpstream<Items,int> {
                p.Wrap(r,c); return Run(r,n,c);
            }
            private async System.Collections.Generic.IAsyncEnumerable<int> Run<N>(Items r,N n,
                [global::System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken c) where N : IStreamUpstream<Items,int> {
                p.WrapperBefore(r,c);
                try {
                    var changed=new Items(9);
                    var first=p.ObserveOpen(changed,n,p.FirstToken);
                    var cursor=first.GetAsyncEnumerator();
                    try {
                        _=await cursor.MoveNextAsync().ConfigureAwait(false);
                        throw new InvalidOperationException("The first upstream must fail.");
                    } catch(FormatException error) {
                        p.WrapperCatch(r,c,first.Invocation,error);
                    } finally {
                        await cursor.DisposeAsync().ConfigureAwait(false);
                        p.WrapperUpstreamClosed(r,c,first.Invocation);
                    }
                    var retry=p.ObserveOpen(changed,n,p.SecondToken);
                    await foreach(var value in retry.ConfigureAwait(false)) {
                        p.WrapperItem(r,c,value,retry.Invocation); yield return value;
                    }
                } finally { p.WrapperFinally(r,c); }
            }
        }
        """;
    private sealed record WrapperRetryResult(int[] Values, StreamSeedEvent[] Events,
        WrapperInvocationOperation[] UpstreamOperations, WrapperTraceOperation[] OuterOperations,
        int Starts, int Moves, int Reads, int Disposals);

    [Fact]
    public Task Stream_wrapper_retry_failure_cleanup_before_reopen_matches_reference()
        => RunWrapperRetry(Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_C08_RETRY_CASE"));
    internal static Task ReplayStreamWrapperRetryCase(string id)
    { ArgumentException.ThrowIfNullOrEmpty(id); return RunWrapperRetry(id); }

    private static async Task RunWrapperRetry(string? id)
    {
        if (!string.IsNullOrEmpty(id) && id != WrapperRetryProfile) throw new ArgumentException("Choose the single fixed retry profile.", nameof(id));
        WriteWrapperRetryArtifact("profile.json", new
        {
            id = WrapperRetryProfile,
            cases = 1,
            input = new[] { 1 },
            outerEnumerationsPerSeries = 1,
            firstFailureInvocation = 2,
            failurePosition = "first source Move,ValueTask.FromException",
            reopenAfterExplicitDispose = true,
            series = new[] { "reference", "canonical" },
            candidateOnly = true,
            productionEnabled = false
        });
        var fixture = FallbackFixtureFor("wrapper-retry"); Assert.Null(fixture.Fused);
        var reference = await RunWrapperRetryBranch(fixture, "reference");
        var canonical = await RunWrapperRetryBranch(fixture, "canonical");
        WriteWrapperRetryArtifact("comparison.json", new
        {
            profile = WrapperRetryProfile,
            reference,
            canonical,
            candidateOnly = true,
            productionEnabled = false,
            scope = "one synchronous source fault/catch/close/reopen"
        });
        Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Events, canonical.Events);
        Assert.Equal(reference.UpstreamOperations, canonical.UpstreamOperations); Assert.Equal(reference.OuterOperations, canonical.OuterOperations);
        Assert.Equal(reference with
        {
            Values = canonical.Values,
            Events = canonical.Events,
            UpstreamOperations = canonical.UpstreamOperations,
            OuterOperations = canonical.OuterOperations
        }, canonical);
        CheckWrapperRetry(reference); CheckWrapperRetry(canonical);
    }
    private static void WriteWrapperRetryArtifact(string name, object data)
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(evidence)) return;
        var path = Path.Combine(evidence, "stream-wrapper-retry", name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }
    private static async Task<WrapperRetryResult> RunWrapperRetryBranch(FallbackFixture fixture, string backend)
    {
        using var api = new CancellationTokenSource(); using var firstToken = new CancellationTokenSource(); using var secondToken = new CancellationTokenSource();
        var probe = new CanonicalStreamWrapperEventProbe
        {
            ApiToken = api.Token,
            FirstToken = firstToken.Token,
            SecondToken = secondToken.Token,
            Input = [1],
            FailInvocation = 2
        };
        var request = Activator.CreateInstance(fixture.Request, [1])!;
        var services = new ServiceCollection(); var roles = new[] { "H", "F1", "W", "F2" };
        for (var i = 0; i < fixture.Services.Length; i++)
        { var type = fixture.Services[i]; var role = roles[i]; services.AddTransient(type, _ => { probe.Resolve(role); return Activator.CreateInstance(type, probe)!; }); }
        using var provider = services.BuildServiceProvider(); IAsyncEnumerable<int> stream;
        if (backend == "reference")
        {
            var bridge = Activator.CreateInstance(fixture.Bridge, provider)!;
            var start = fixture.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(fixture.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<int>)), bridge);
            stream = (IAsyncEnumerable<int>)typeof(CanonicalStreamWrapperReference).GetMethod("FilterWrapperFilter")!
                .MakeGenericMethod(fixture.Request, typeof(int), fixture.Services[1], fixture.Services[2], fixture.Services[3])
                .Invoke(null, [provider, request, start, api.Token, null])!;
        }
        else stream = (IAsyncEnumerable<int>)fixture.Canonical.Invoke(null, [provider, request, api.Token])!;
        var observed = probe.ObserveOuter(stream, request, api.Token); var values = new List<int>(); var operations = new List<WrapperTraceOperation>();
        var complete = false;
        try
        {
            Assert.Empty(probe.Snapshot()); var cursor = observed.GetAsyncEnumerator(); var outer = probe.LastOuterInvocation;
            for (var sequence = 0; sequence < 2; sequence++)
            {
                var move = cursor.MoveNextAsync();
                var operation = new WrapperTraceOperation("Move", outer, sequence, move.IsCompleted, move.IsCompletedSuccessfully, move.IsCanceled, move.IsFaulted, null);
                operations.Add(operation); Assert.True(move.IsCompleted); var ok = await move; operations[^1] = operation with { Value = ok };
                Assert.Equal(sequence == 0, ok);
                if (ok)
                { var reads = probe.Reads; var item = cursor.Current; _ = cursor.Current; Assert.Equal(reads, probe.Reads); values.Add(item); probe.ConsumerYield(outer, item); }
            }
            var dispose = cursor.DisposeAsync();
            operations.Add(new("Dispose", outer, 0, dispose.IsCompleted, dispose.IsCompletedSuccessfully, dispose.IsCanceled, dispose.IsFaulted, null));
            Assert.True(dispose.IsCompleted); await dispose;
            complete = true;
            return new([.. values], probe.Snapshot(), probe.OperationSnapshot(), [.. operations], probe.Starts, probe.Moves, probe.Reads, probe.Disposals);
        }
        finally
        {
            WriteWrapperRetryArtifact(backend + ".json", new
            {
                profile = WrapperRetryProfile,
                backend,
                complete,
                values,
                events = probe.Snapshot(),
                upstreamOperations = probe.OperationSnapshot(),
                outerOperations = operations,
                probe.Starts,
                probe.Moves,
                probe.Reads,
                probe.Disposals
            });
        }
    }
    private static void CheckWrapperRetry(WrapperRetryResult result)
    {
        Assert.Equal(new[] { 1 }, result.Values); Assert.Equal(2, result.Starts); Assert.Equal(3, result.Moves);
        Assert.Equal(1, result.Reads); Assert.Equal(2, result.Disposals);
        var events = result.Events;
        Assert.Equal(new[] { 2, 3 }, events.Where(e => e.Kind == "UpstreamOpen").Select(e => e.Invocation));
        Assert.All(events, e =>
        {
            Assert.Equal(e.Invocation == 1 ? 0 : 1, e.ParentInvocation);
            Assert.Equal(e.Invocation == 1 ? 1 : 2, e.Request); Assert.Equal(e.Invocation, e.Token);
        });
        var failure = Assert.Single(events, e => e.Kind == "Throw"); Assert.Equal(2, failure.Invocation); Assert.Equal("H.Move", failure.Stage);
        Assert.Equal(0, failure.Sequence); Assert.Equal(1, failure.Error); Assert.Equal("System.FormatException", failure.ErrorType);
        var caught = Assert.Single(events, e => e.Kind == "WrapperCatch"); Assert.Equal(1, caught.Invocation); Assert.Equal(2, caught.RelatedInvocation);
        Assert.Equal(failure.Error, caught.Error); Assert.Equal(failure.ErrorType, caught.ErrorType);
        var closed = Assert.Single(events, e => e.Kind == "WrapperUpstreamClosed"); Assert.Equal(1, closed.Invocation); Assert.Equal(2, closed.RelatedInvocation);
        var first = events.Where(e => e.Invocation == 2).ToArray(); var second = events.Where(e => e.Invocation == 3).ToArray();
        Assert.DoesNotContain(first, e => (e.Kind is "InnerCurrent" or "Filter") || e.Kind == "Resolve" && e.Stage == "F1");
        Assert.Single(first, e => e.Kind == "InnerDisposeStart"); var firstEnd = Assert.Single(first, e => e.Kind == "InnerDisposeEnd");
        Assert.Single(first, e => e.Kind == "EnumeratorDispose"); var disposed = Assert.Single(first, e => e.Kind == "EnumeratorDisposed");
        var reopen = Assert.Single(second, e => e.Kind == "UpstreamOpen");
        Assert.True(Array.IndexOf(events, failure) < Array.IndexOf(events, firstEnd));
        Assert.True(Array.IndexOf(events, firstEnd) < Array.IndexOf(events, caught));
        Assert.True(Array.IndexOf(events, caught) < Array.IndexOf(events, disposed));
        Assert.True(Array.IndexOf(events, disposed) < Array.IndexOf(events, closed) && Array.IndexOf(events, closed) < Array.IndexOf(events, reopen));
        Assert.Single(second, e => e.Kind == "InnerDisposeEnd"); Assert.Single(second, e => e.Kind == "Resolve" && e.Stage == "F1");
        Assert.Single(second, e => e.Kind == "Filter" && e.Stage == "F1");
        var final = Assert.Single(events, e => e.Kind == "WrapperFinally");
        Assert.True(Array.FindIndex(events, e => e.Invocation == 3 && e.Kind == "InnerDisposeEnd") < Array.IndexOf(events, final));
        Assert.DoesNotContain(events, e => e.Kind is "WrapperDrop" or "InnerDisposeThrow" or "Map");
        Assert.Equal(5, result.UpstreamOperations.Length); Assert.All(result.UpstreamOperations, o => { Assert.False(o.CallsiteThrow); Assert.True(o.Completed); Assert.False(o.Canceled); });
        var fault = Assert.Single(result.UpstreamOperations, o => o.Kind == "Move" && o.Invocation == 2);
        Assert.True(fault.Faulted); Assert.False(fault.CompletedSuccessfully); Assert.Equal(0, fault.Error);
        Assert.All(result.UpstreamOperations.Where(o => !ReferenceEquals(o, fault)), o => { Assert.True(o.CompletedSuccessfully); Assert.False(o.Faulted); });
        Assert.Equal(3, result.OuterOperations.Length); Assert.All(result.OuterOperations, o => { Assert.True(o.Completed); Assert.True(o.CompletedSuccessfully); Assert.False(o.Faulted); Assert.False(o.Canceled); });
    }
}
