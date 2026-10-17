using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record StreamSeedOperation(string Operation, int Sequence, bool CallsiteThrow,
        bool Completed, bool CompletedSuccessfully, bool Canceled, bool Faulted, bool? Value, int Error, string? ErrorType);
    private sealed record StreamSeedResult(int[] Values, StreamSeedEvent[] Events, StreamSeedOperation[] Operations,
        int Starts, int Moves, int Reads, int Disposals, int Effects, int FirstFactories, int LastFactories,
        int Error, string? ErrorType, bool ExceptionIdentityPreserved);

    [Fact]
    public Task Stream_seed_corpus_common_events_match_generated_candidates()
        => RunStreamSeedCases(Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_C08_CASE"));

    // Standalone entry: one known ID selects exactly one saved corpus input/profile and three branches.
    // The same entry is available from the Fact via ZENDIATOR_STREAM_C08_CASE, with no other pipelines run.
    internal static Task ReplayStreamSeedCase(string caseId)
    {
        ArgumentException.ThrowIfNullOrEmpty(caseId);
        return RunStreamSeedCases(caseId);
    }

    private static async Task RunStreamSeedCases(string? caseId)
    {
        var corpus = CanonicalStreamSeedCorpus.Create(); Assert.Equal(CanonicalStreamSeedCorpus.Count, corpus.Length);
        Assert.Equal(corpus.Length, corpus.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { -2, 4, -3, 1 }, corpus.Single(c => c.Id == "seed-4").Input);
        Assert.Equal(new[] { -4, -2, 3, -1, -3, -4, 2, 4, 4, -3, 1, 2, 1, 2, -2, 2 }, corpus.Single(c => c.Id == "seed-16").Input);
        var selected = string.IsNullOrEmpty(caseId) ? corpus : corpus.Where(c => c.Id == caseId).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Choose one of the eight fixed corpus IDs.", nameof(caseId));
        WriteSeedArtifact("corpus.json", new
        {
            seed = CanonicalStreamSeedCorpus.Seed,
            generatorVersion = CanonicalStreamSeedCorpus.Version,
            algorithm = "xorshift32:uint shifts13/17/5;value=(int)(state%9)-4;one stream draws0..19",
            stageOrder = new[] { "F1", "Map", "F2" },
            corpus,
            selectedIds = selected.Select(c => c.Id),
            selectedCaseCount = selected.Length,
            branchExecutions = selected.Length * 3,
            newContractComparisons = selected.Length * 2,
            replaySelector = "ZENDIATOR_STREAM_C08_CASE=<known ID> or ReplayStreamSeedCase(ID)",
            shape = "closed reference request/int item/borrowed class/sync hooks/None token/full consumption",
            candidateOnly = true,
            productionEnabled = false,
            measurements = 0
        });
        var fixture = FallbackFixtureFor("int"); Assert.NotNull(fixture.Fused);
        foreach (var input in selected)
        {
            var reference = await RunSeedBranch(fixture, input, "reference");
            var canonical = await RunSeedBranch(fixture, input, "canonical");
            var fused = await RunSeedBranch(fixture, input, "fused");
            WriteSeedArtifact(input.Id + "/comparison.json", new
            {
                input,
                reference,
                canonical,
                fused,
                candidateOnly = true,
                productionEnabled = false,
                sameContract = true
            });
            Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Values, fused.Values);
            Assert.Equal(reference.Events, canonical.Events); Assert.Equal(reference.Events, fused.Events);
            Assert.Equal(reference.Operations, canonical.Operations); Assert.Equal(reference.Operations, fused.Operations);
            Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events, Operations = canonical.Operations }, canonical);
            Assert.Equal(reference with { Values = fused.Values, Events = fused.Events, Operations = fused.Operations }, fused);
            CheckSeedContract(input, reference); CheckSeedContract(input, canonical); CheckSeedContract(input, fused);
        }
    }

    private static void WriteSeedArtifact(string name, object data)
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(evidence)) return;
        var path = Path.Combine(evidence, "stream-seed-trace", name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(data));
    }

    private static async Task<StreamSeedResult> RunSeedBranch(FallbackFixture f, StreamSeedCase input, string backend)
    {
        var request = Activator.CreateInstance(f.Request, [1])!;
        var events = new StreamSeedEvents(request, default);
        var p = new CanonicalStreamFallbackProbe { Input = (int[])input.Input.Clone(), Outer = request, CanonicalEvent = events.Add };
        if (input.Id == "filter-failure") p.FirstError = new FormatException("seed-filter");
        var factoryError = new InvalidOperationException("seed-factory");
        Exception? expectedIdentity = input.Id == "filter-failure" ? p.FirstError : input.Id == "factory-failure" ? factoryError : null;
        var collection = new ServiceCollection(); var roles = new[] { "H", "F1", "Map", "F2" };
        for (var i = 0; i < f.Services.Length; i++)
        {
            var type = f.Services[i]; var role = roles[i];
            collection.AddTransient(type, _ =>
            {
                p.Resolve(role);
                if (input.Id == "factory-failure" && role == "F1")
                {
                    events.Add(new("Throw", "F1.factory", request, default, Error: factoryError));
                    throw factoryError;
                }
                return Activator.CreateInstance(type, p)!;
            });
        }
        using var provider = collection.BuildServiceProvider();
        var saved = CanonicalStreamFallbackProbe.Ambient.Value; var savedCulture = CultureInfo.CurrentCulture;
        var savedContext = SynchronizationContext.Current;
        var values = new List<int>(); var operations = new List<StreamSeedOperation>(); Exception? failure = null;
        IAsyncEnumerator<int>? cursor = null; var ended = false; var complete = false;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null); CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CanonicalStreamFallbackProbe.Ambient.Value = "c08-caller";
            IAsyncEnumerable<int> stream;
            if (backend == "reference")
            {
                var bridge = Activator.CreateInstance(f.Bridge, provider)!;
                var start = f.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(f.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<int>)), bridge);
                stream = (IAsyncEnumerable<int>)typeof(CanonicalStreamItemReference).GetMethod("FilterMapFilter")!
                    .MakeGenericMethod(f.Request, typeof(int), f.Services[1], f.Services[2], f.Services[3])
                    .Invoke(null, [provider, request, start, default(CancellationToken), null])!;
            }
            else stream = (IAsyncEnumerable<int>)(backend == "fused" ? f.Fused! : f.Canonical)
                .Invoke(null, [provider, request, default(CancellationToken)])!;
            cursor = stream.GetAsyncEnumerator(); Assert.Empty(events.Snapshot());
            // At most input length yields plus one termination; no unbounded fuzz/consumer loop.
            for (var sequence = 0; sequence <= input.Input.Length; sequence++)
            {
                ValueTask<bool> move;
                try { move = cursor.MoveNextAsync(); }
                catch (Exception error)
                {
                    failure = error;
                    operations.Add(new("Move", sequence, true, false, false, false, false, null, events.ErrorId(error), error.GetType().FullName));
                    break;
                }
                var completed = move.IsCompleted; var successful = move.IsCompletedSuccessfully;
                var canceled = move.IsCanceled; var faulted = move.IsFaulted;
                Assert.True(completed, "This bounded sync corpus must not suspend.");
                bool hasItem;
                try { hasItem = await move; }
                catch (Exception error)
                {
                    failure = error;
                    operations.Add(new("Move", sequence, false, completed, successful, canceled, faulted, null, events.ErrorId(error), error.GetType().FullName));
                    break;
                }
                operations.Add(new("Move", sequence, false, completed, successful, canceled, faulted, hasItem, 0, null));
                Assert.Equal("c08-caller", CanonicalStreamFallbackProbe.Ambient.Value);
                if (!hasItem) { ended = true; break; }
                var reads = p.Reads; var value = cursor.Current; _ = cursor.Current; Assert.Equal(reads, p.Reads);
                values.Add(value); events.Add(new("Yield", "consumer", request, default, p.Reads - 1, Output: value));
            }
            Assert.True(ended || failure != null, "The finite input must end or reach its declared failure.");
            ValueTask dispose;
            try { dispose = cursor.DisposeAsync(); }
            catch (Exception error)
            {
                operations.Add(new("Dispose", 0, true, false, false, false, false, null, events.ErrorId(error), error.GetType().FullName));
                throw;
            }
            var disposeCompleted = dispose.IsCompleted; var disposeSuccessful = dispose.IsCompletedSuccessfully;
            var disposeCanceled = dispose.IsCanceled; var disposeFaulted = dispose.IsFaulted;
            Assert.True(disposeCompleted, "This corpus cleanup must not suspend.");
            try { await dispose; }
            catch (Exception error)
            {
                operations.Add(new("Dispose", 0, false, disposeCompleted, disposeSuccessful, disposeCanceled, disposeFaulted,
                    null, events.ErrorId(error), error.GetType().FullName));
                throw;
            }
            operations.Add(new("Dispose", 0, false, disposeCompleted, disposeSuccessful, disposeCanceled, disposeFaulted, null, 0, null));
            Assert.Equal("c08-caller", CanonicalStreamFallbackProbe.Ambient.Value);
            if (failure != null)
            {
                Assert.Same(events.FirstError, failure);
                if (expectedIdentity != null) Assert.Same(expectedIdentity, failure);
            }
            complete = true;
            return new([.. values], events.Snapshot(), [.. operations], p.Starts, p.Moves, p.Reads, p.Disposals,
                p.Effects, p.BeforeResolves, p.AfterResolves, events.ErrorId(failure), failure?.GetType().FullName,
                failure == null || ReferenceEquals(events.FirstError, failure));
        }
        finally
        {
            // Preserve partial events/operations even if a newly observed difference aborts the run.
            WriteSeedArtifact(input.Id + "/" + backend + ".json", new
            {
                input,
                backend,
                complete,
                values,
                events = events.Snapshot(),
                operations,
                p.Starts,
                p.Moves,
                p.Reads,
                p.Disposals,
                p.Effects,
                p.BeforeResolves,
                p.AfterResolves,
                error = failure?.GetType().FullName,
                exceptionIdentityPreserved = failure == null || ReferenceEquals(events.FirstError, failure)
            });
            CanonicalStreamFallbackProbe.Ambient.Value = saved; CultureInfo.CurrentCulture = savedCulture;
            SynchronizationContext.SetSynchronizationContext(savedContext);
        }
    }

    private static void CheckSeedContract(StreamSeedCase input, StreamSeedResult result)
    {
        Assert.Equal(1, result.Starts); Assert.Equal(1, result.Disposals); Assert.True(result.ExceptionIdentityPreserved);
        Assert.All(result.Events, e => { Assert.Equal(1, e.Invocation); Assert.Equal(1, e.Request); Assert.Equal(0, e.Token); });
        Assert.Single(result.Events, e => e.Kind == "HandlerStart");
        Assert.Single(result.Events, e => e.Kind == "InnerDisposeStart"); Assert.Single(result.Events, e => e.Kind == "InnerDisposeEnd");
        Assert.DoesNotContain(result.Events, e => e.Kind == "InnerDisposeThrow");
        Assert.Equal(result.Reads, result.Events.Count(e => e.Kind == "InnerCurrent"));
        Assert.Equal(result.Moves, result.Events.Count(e => e.Kind == "InnerMoveNext"));
        Assert.Equal(result.Values.Length, result.Events.Count(e => e.Kind == "Yield"));
        Assert.All(result.Events.Where(e => e.Kind == "Filter"), e =>
            Assert.Equal(e.Stage == "F1" ? e.Input!.Value > 0 : e.Input!.Value < 4, e.Result!.Value));
        Assert.All(result.Events.Where(e => e.Kind == "Map"), e => Assert.Equal(checked(e.Input!.Value * 2), e.Output!.Value));
        Assert.Single(result.Events, e => e.Kind == "Resolve" && e.Stage == "H");
        Assert.Equal(result.FirstFactories, result.Events.Count(e => e.Kind == "Resolve" && e.Stage == "F1"));
        Assert.Equal(result.LastFactories, result.Events.Count(e => e.Kind == "Resolve" && e.Stage == "F2"));
        var reachesMap = input.Id is "filter-failure" or "factory-failure" ? false : input.Input.Any(x => x > 0);
        Assert.Equal(reachesMap ? 1 : 0, result.Events.Count(e => e.Kind == "Resolve" && e.Stage == "Map"));
        Assert.Equal(result.Effects, result.Events.Count(e => e.Kind == "Map" || e.Kind == "Throw" && e.Stage == "Map"));
        Assert.Equal(Enumerable.Range(0, result.Moves), result.Events.Where(e => e.Kind == "InnerMoveNext").Select(e => e.Sequence!.Value));
        Assert.Equal(Enumerable.Range(0, result.Reads), result.Events.Where(e => e.Kind == "InnerCurrent").Select(e => e.Sequence!.Value));
        Assert.Equal(input.Input.Take(result.Reads), result.Events.Where(e => e.Kind == "InnerCurrent").Select(e => e.Output!.Value));
        foreach (var first in result.Events.Where(e => e.Kind == "Filter" && e.Stage == "F1"))
        {
            var current = Array.FindIndex(result.Events, e => e.Kind == "InnerCurrent" && e.Sequence == first.Sequence);
            var firstIndex = Array.IndexOf(result.Events, first); Assert.True(current >= 0 && current < firstIndex);
            if (first.Result == false)
                Assert.DoesNotContain(result.Events, e => e.Sequence == first.Sequence && (e.Stage is "Map" or "F2"));
        }
        foreach (var mapped in result.Events.Where(e => e.Kind == "Map"))
        {
            var first = Array.FindIndex(result.Events, e => e.Kind == "Filter" && e.Stage == "F1" && e.Sequence == mapped.Sequence && e.Result == true);
            var last = Array.FindIndex(result.Events, e => e.Kind == "Filter" && e.Stage == "F2" && e.Sequence == mapped.Sequence && e.Input == mapped.Output);
            var mapIndex = Array.IndexOf(result.Events, mapped); Assert.True(first >= 0 && first < mapIndex && mapIndex < last);
        }
        foreach (var yielded in result.Events.Where(e => e.Kind == "Yield"))
        {
            var last = Array.FindIndex(result.Events, e => e.Kind == "Filter" && e.Stage == "F2" && e.Sequence == yielded.Sequence && e.Result == true && e.Input == yielded.Output);
            Assert.True(last >= 0 && last < Array.IndexOf(result.Events, yielded));
        }
        Assert.All(result.Operations, o => { Assert.False(o.CallsiteThrow); Assert.True(o.Completed); Assert.False(o.Canceled); });
        var dispose = Assert.Single(result.Operations, o => o.Operation == "Dispose"); Assert.True(dispose.CompletedSuccessfully);
        Assert.Equal(0, dispose.Error); Assert.False(dispose.Faulted);
        if (input.FailureStage == null)
        {
            Assert.Equal(input.Input.Length, result.Reads); Assert.Equal(input.Input.Length + 1, result.Moves);
            // Declared algebra: positive int x doubles; only x=1 passes the final mapped-value filter.
            Assert.Equal(Enumerable.Repeat(2, input.Input.Count(x => x == 1)), result.Values);
            Assert.Equal(input.Input.Count(x => x > 0), result.Effects);
            Assert.Equal(input.Input.Length == 0 ? 0 : 1, result.FirstFactories);
            Assert.Equal(input.Input.Any(x => x > 0) ? 1 : 0, result.LastFactories);
            Assert.Equal(0, result.Error); Assert.Null(result.ErrorType); Assert.DoesNotContain(result.Events, e => e.Kind == "Throw");
            Assert.All(result.Operations, o => { Assert.True(o.CompletedSuccessfully); Assert.False(o.Faulted); Assert.Equal(0, o.Error); });
            Assert.False(result.Operations.Last(o => o.Operation == "Move").Value!.Value);
            if (!input.Input.Any(x => x > 0))
                Assert.DoesNotContain(result.Events, e => e.Stage is "Map" or "F2");
        }
        else
        {
            Assert.Empty(result.Values); Assert.Equal(1, result.Reads); Assert.Equal(1, result.Moves);
            Assert.Equal(1, result.FirstFactories); Assert.Equal(0, result.LastFactories);
            Assert.Equal(input.Id == "overflow" ? 1 : 0, result.Effects);
            var error = Assert.Single(result.Events, e => e.Kind == "Throw");
            Assert.Equal(input.FailureStage, error.Stage); Assert.Equal(input.FailureType, error.ErrorType);
            Assert.Equal(1, error.Error); Assert.Equal(error.Error, result.Error); Assert.Equal(input.FailureType, result.ErrorType);
            Assert.DoesNotContain(result.Events, e => e.Stage == "F2");
            if (input.Id == "factory-failure") Assert.DoesNotContain(result.Events, e => e.Kind == "Filter");
            if (input.Id != "overflow") Assert.DoesNotContain(result.Events, e => e.Stage == "Map");
            var move = Assert.Single(result.Operations, o => o.Operation == "Move");
            Assert.True(move.Faulted); Assert.False(move.CompletedSuccessfully); Assert.Equal(1, move.Error);
            var snapshot = result.Events;
            Assert.True(Array.FindIndex(snapshot, e => e.Kind == "Throw") < Array.FindIndex(snapshot, e => e.Kind == "InnerDisposeStart"));
        }
    }
}
