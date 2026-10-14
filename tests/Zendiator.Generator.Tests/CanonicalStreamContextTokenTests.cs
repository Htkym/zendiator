using System.Collections.Concurrent;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed class StreamTrackingContext(string name) : SynchronizationContext
    {
        public readonly string Name = name;
        private int _posts;
        public int Posts => Volatile.Read(ref _posts);
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posts);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var before = Current; SetSynchronizationContext(this);
                try { callback(state); } finally { SetSynchronizationContext(before); }
            });
        }
    }
    private sealed record StreamEnvironmentObservation(string Phase, string? Ambient, string Context, bool ContextIdentity,
        bool FlowSuppressed, string Token, bool? EffectiveTokenIdentity, bool RequestIdentity);
    private sealed record StreamEnvironmentResult(int[] Values, StreamEnvironmentObservation[] Observations, int Moves, int Reads,
        int Disposals, int MoveConsumptions, int DisposeConsumptions, string Error, bool Canceled, bool Faulted, string ErrorToken,
        int LinkedCallbacksAfterExit, string EffectiveToken, bool? ErrorTokenMatchesEffective, int ContextPosts);

    [Theory]
    [InlineData("none-sync-context")] [InlineData("api-suppression")] [InlineData("enum-delivery")]
    [InlineData("same-pending-context")] [InlineData("distinct-delivery")]
    [InlineData("distinct-pre-api")] [InlineData("distinct-pre-enum")]
    [InlineData("distinct-pending-api")] [InlineData("distinct-pending-enum")]
    [InlineData("distinct-between-enum")] [InlineData("distinct-start-failure")]
    [InlineData("distinct-pending-failure-cleanup")]
    public async Task Stream_selected_context_token_and_failure_representatives_match(string profile)
    {
        async Task<StreamEnvironmentResult> Run(bool generated)
        {
            using var api = new CancellationTokenSource(); using var enumeration = new CancellationTokenSource();
            var apiToken = profile.StartsWith("none-", StringComparison.Ordinal) || profile == "enum-delivery" ? default : api.Token;
            var enumToken = profile is "none-sync-context" or "api-suppression" ? default : profile == "same-pending-context" ? api.Token : enumeration.Token;
            var moveGate = new ContractOnce<bool>(); var disposeGate = new ContractOnce<bool>();
            var pendingContext = profile == "same-pending-context";
            var pendingFailure = profile == "distinct-pending-failure-cleanup";
            var pendingCancel = profile.StartsWith("distinct-pending-", StringComparison.Ordinal) && !pendingFailure;
            var contextWrites = profile == "none-sync-context" || pendingContext;
            var contexts = new ConcurrentDictionary<string, StreamTrackingContext>();
            var observations = new List<StreamEnvironmentObservation>();
            var sourceError = new FormatException("source"); var cleanupError = new InvalidOperationException("cleanup");
            var startupError = new ApplicationException("startup");
            var probe = new CanonicalStreamPrototypeProbe { Input = [1, 1], GetEnumeratorError = profile == "distinct-start-failure" ? startupError : null,
                CleanupError = pendingFailure ? cleanupError : null };
            CancellationToken? handlerEffectiveToken = null;
            string Token(CancellationToken value) => !value.CanBeCanceled ? "none" : value == apiToken ? "api" : value == enumToken ? "enum" : "linked";
            void Observe(string phase, object? request, CancellationToken token)
            {
                if (phase == "handler.enter") handlerEffectiveToken = token;
                var userToken = !phase.StartsWith("consumer.", StringComparison.Ordinal) && !phase.StartsWith("factory:", StringComparison.Ordinal);
                var current = SynchronizationContext.Current;
                var tag = current is StreamTrackingContext tracked ? tracked.Name : current == null ? "none" : "external";
                var identity = current is not StreamTrackingContext instance || contexts.TryGetValue(instance.Name, out var known) && ReferenceEquals(instance, known);
                observations.Add(new(phase, CanonicalStreamPrototypeProbe.Ambient.Value, tag, identity,
                    ExecutionContext.IsFlowSuppressed(), Token(token), userToken && handlerEffectiveToken.HasValue ? token.Equals(handlerEffectiveToken.Value) : null,
                    request == null || ReferenceEquals(request, probe.Request)));
                if (contextWrites && !phase.StartsWith("consumer.", StringComparison.Ordinal) &&
                    (phase.EndsWith(".enter", StringComparison.Ordinal) || phase.EndsWith(".resume", StringComparison.Ordinal)))
                {
                    CanonicalStreamPrototypeProbe.Ambient.Value = phase;
                    SynchronizationContext.SetSynchronizationContext(contexts.GetOrAdd(phase, n => new(n)));
                }
            }
            probe.Observe = Observe;
            async ValueTask<bool> PendingMove()
            {
                Observe("source.await-move.enter", probe.Request, probe.EnumeratorToken);
                var value = pendingContext ? await moveGate.Task : await moveGate.Task.ConfigureAwait(false);
                Observe("source.await-move.resume", probe.Request, probe.EnumeratorToken); return value;
            }
            async ValueTask PendingDispose()
            {
                Observe("source.await-dispose.enter", probe.Request, probe.EnumeratorToken);
                await disposeGate.Void;
                Observe("source.await-dispose.resume", probe.Request, probe.EnumeratorToken);
            }
            if (pendingContext || pendingCancel || pendingFailure) probe.PendingMove = PendingMove;
            if (pendingContext) probe.PendingDispose = PendingDispose;
            if (profile == "distinct-pre-api") api.Cancel();
            if (profile == "distinct-pre-enum") enumeration.Cancel();
            var beforeAmbient = CanonicalStreamPrototypeProbe.Ambient.Value; var beforeContext = SynchronizationContext.Current;
            if (profile != "api-suppression") CanonicalStreamPrototypeProbe.Ambient.Value = "caller-a";
            var callerContext = contextWrites ? contexts.GetOrAdd("caller", n => new(n)) : null;
            var (services, cursor) = PrototypeCursor(probe, generated, apiToken, enumToken); using var provider = services;
            var values = new List<int>(); Exception? failure = null; bool canceled = false, faulted = false;
            try
            {
                Observe("consumer.create", null, default);
                for (var step = 0; step < 3; step++)
                {
                    if (step == 1 && profile == "distinct-between-enum") enumeration.Cancel();
                    SynchronizationContext.SetSynchronizationContext(callerContext ?? beforeContext);
                    CanonicalStreamPrototypeProbe.Ambient.Value = step == 0 ? CanonicalStreamPrototypeProbe.Ambient.Value : "caller-b";
                    Observe("consumer.move.enter", null, default);
                    var move = cursor.MoveNextAsync();
                    Observe("consumer.move.return", null, default);
                    Assert.Same(callerContext ?? beforeContext, SynchronizationContext.Current);
                    SynchronizationContext.SetSynchronizationContext(beforeContext);
                    var completion = move.AsTask();
                    if (step == 0 && (pendingContext || pendingCancel || pendingFailure))
                    {
                        Assert.False(completion.IsCompleted); Assert.Equal(0, probe.Reads); Assert.Equal(0, moveGate.Consumptions);
                        if (pendingCancel)
                        {
                            if (profile.EndsWith("-api", StringComparison.Ordinal)) api.Cancel(); else enumeration.Cancel();
                            Assert.True(handlerEffectiveToken.HasValue && handlerEffectiveToken.Value.IsCancellationRequested);
                            moveGate.Fail(new OperationCanceledException(probe.EnumeratorToken));
                        }
                        else if (pendingFailure) moveGate.Fail(sourceError);
                        else await Task.Run(() => { CanonicalStreamPrototypeProbe.Ambient.Value = "foreign-releaser"; moveGate.Complete(true); });
                    }
                    try
                    {
                        var hasItem = await completion.WaitAsync(TimeSpan.FromSeconds(5));
                        Observe("consumer.move.completed", null, default);
                        if (!hasItem) break; values.Add(cursor.Current);
                    }
                    catch (Exception error)
                    { failure = error; canceled = completion.IsCanceled; faulted = completion.IsFaulted; Observe("consumer.move.failed", null, default); break; }
                    if (pendingContext) break; // explicit pending Dispose below
                }
                SynchronizationContext.SetSynchronizationContext(callerContext ?? beforeContext);
                CanonicalStreamPrototypeProbe.Ambient.Value = "dispose-caller"; Observe("consumer.dispose.enter", null, default);
                var disposing = cursor.DisposeAsync(); Observe("consumer.dispose.return", null, default);
                Assert.Same(callerContext ?? beforeContext, SynchronizationContext.Current); SynchronizationContext.SetSynchronizationContext(beforeContext);
                var disposed = disposing.AsTask();
                if (pendingContext)
                {
                    Assert.False(disposed.IsCompleted); Assert.Equal(0, disposeGate.Consumptions);
                    await Task.Run(() => { CanonicalStreamPrototypeProbe.Ambient.Value = "foreign-disposer"; disposeGate.Complete(true); });
                }
                await disposed.WaitAsync(TimeSpan.FromSeconds(5)); Observe("consumer.dispose.completed", null, default);
                await cursor.DisposeAsync();
                var effective = probe.StartToken;
                if (failure is OperationCanceledException canceledError && !effective.CanBeCanceled) effective = canceledError.CancellationToken;
                var expectedToken = profile == "none-sync-context" ? "none" : profile == "enum-delivery" ? "enum" :
                    profile is "api-suppression" or "same-pending-context" ? "api" : "linked";
                Assert.Equal(expectedToken, Token(effective));
                Assert.All(observations.Where(o => !o.Phase.StartsWith("consumer.", StringComparison.Ordinal) &&
                    !o.Phase.StartsWith("factory:", StringComparison.Ordinal)), o =>
                    { Assert.Equal(expectedToken, o.Token); Assert.Equal(true, o.EffectiveTokenIdentity); });
                bool? errorTokenMatchesEffective = failure is OperationCanceledException errorWithToken && handlerEffectiveToken.HasValue ?
                    errorWithToken.CancellationToken.Equals(handlerEffectiveToken.Value) : null;
                if (failure is OperationCanceledException && handlerEffectiveToken.HasValue) Assert.Equal(true, errorTokenMatchesEffective);
                if (profile == "api-suppression") Assert.Null(observations[0].Ambient);
                var callbacks = 0; using var registration = effective.Register(() => callbacks++);
                var immediateCallbacks = callbacks;
                api.Cancel(); enumeration.Cancel();
                var callbacksAfterExit = callbacks - immediateCallbacks;
                var errorIdentity = failure == null ? "none" : ReferenceEquals(failure, startupError) ? "startup" :
                    ReferenceEquals(failure, cleanupError) ? "cleanup" : failure is OperationCanceledException ? "cancel" : "unexpected";
                Assert.DoesNotContain("unexpected", errorIdentity);
                Assert.All(observations, o => { Assert.True(o.ContextIdentity); Assert.True(o.RequestIdentity); });
                if (Token(effective) == "linked") Assert.Equal(0, callbacksAfterExit);
                if (pendingContext || pendingCancel || pendingFailure) Assert.Equal(1, moveGate.Consumptions);
                if (pendingContext) { Assert.Equal(1, disposeGate.Consumptions); Assert.Equal(2, contexts.Values.Sum(c => c.Posts)); }
                Assert.Equal(profile.StartsWith("distinct-pre-", StringComparison.Ordinal) || profile == "distinct-start-failure" ? 0 : 1, probe.Disposals);
                if (pendingCancel || profile == "distinct-between-enum") Assert.Equal(1, probe.Moves);
                if (pendingFailure) Assert.Same(cleanupError, failure);
                return new([.. values], [.. observations], probe.Moves, probe.Reads, probe.Disposals, moveGate.Consumptions, disposeGate.Consumptions,
                    errorIdentity, canceled, faulted, failure is OperationCanceledException oce ? Token(oce.CancellationToken) : "none", callbacksAfterExit, Token(effective),
                    errorTokenMatchesEffective, contexts.Values.Sum(c => c.Posts));
            }
            finally { CanonicalStreamPrototypeProbe.Ambient.Value = beforeAmbient; SynchronizationContext.SetSynchronizationContext(beforeContext); }
        }
        async Task<StreamEnvironmentResult> Selected(bool generated)
        {
            if (profile != "api-suppression") return await Run(generated);
            var before = CanonicalStreamPrototypeProbe.Ambient.Value; CanonicalStreamPrototypeProbe.Ambient.Value = "outer-suppressed";
            try
            {
                Task<StreamEnvironmentResult> task;
                using (ExecutionContext.SuppressFlow()) task = Task.Run(() => Run(generated));
                return await task;
            }
            finally { CanonicalStreamPrototypeProbe.Ambient.Value = before; }
        }
        var entryContext = SynchronizationContext.Current;
        async Task<StreamEnvironmentResult> FromSameEntry(bool generated)
        {
            var before = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(entryContext);
                return await Selected(generated);
            }
            finally { SynchronizationContext.SetSynchronizationContext(before); }
        }
        var reference = await FromSameEntry(false); var candidate = await FromSameEntry(true);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence)) File.WriteAllText(Path.Combine(evidence, "stream-prototype", "environment-" + profile + ".json"),
            System.Text.Json.JsonSerializer.Serialize(new { profile, reference, candidate, candidateOnly = true, productionEnabled = false }));
        Assert.Equal(reference.Values, candidate.Values); Assert.Equal(reference.Observations, candidate.Observations);
        Assert.Equal(reference with { Values = candidate.Values, Observations = candidate.Observations }, candidate);
    }
}
