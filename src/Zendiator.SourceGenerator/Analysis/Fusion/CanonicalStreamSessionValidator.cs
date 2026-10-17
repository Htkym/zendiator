using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static Zendiator.SourceGenerator.StreamExecutionNames;

namespace Zendiator.SourceGenerator;

internal sealed record StreamSessionPathEvidence(StreamProgram Program, StreamPhase Terminal, bool Started,
    EquatableArray<int> Acquired, EquatableArray<int> Live, EquatableArray<int> Released,
    EquatableArray<StreamSiteId> Visited, EquatableArray<string> Failures, string? EscapingError, bool Canceled);
internal sealed record StreamSessionInspection(EquatableArray<CanonicalExecutionGraphFact> Facts, EquatableArray<StreamSessionPathEvidence> Paths);

/// <summary>Interprets candidate operands and actual paths; never calls a builder or RequiredStream*.</summary>
internal static class CanonicalStreamSessionValidator
{
    private sealed record Life(ImmutableDictionary<string, string?> Values, ImmutableArray<int> Acquired,
        bool Started = false, bool Ended = false, bool Disposed = false, bool Cache = false, string HookCache = "");
    private sealed record State(StreamSiteId At, Life Life, ImmutableHashSet<int> Guarded, ImmutableHashSet<int> Validated,
        ImmutableArray<int> Expected, ImmutableArray<int> Released, ImmutableArray<StreamSiteId> Visited, ImmutableArray<string> Failures,
        string? Error = null, bool Busy = false, bool PreCancel = false, bool DisposeBegan = false, bool SourceCleaned = false, bool LinkCleaned = false);

    internal static StreamSessionInspection Inspect(StreamSegmentContract contract, CanonicalStreamExecutionSchedule candidate)
    {
        var facts = new HashSet<CanonicalExecutionGraphFact>(); var paths = new List<StreamSessionPathEvidence>();
        var positions = Enumerable.Range(-1, contract.Hooks.Count + 1).Where(i => Sessions(contract, i).Count != 0).ToArray();
        if (positions.Length == 0) return new(default, default);
        void Reject(string reason) => facts.Add(new(reason, contract.Anchor));
        var flows = candidate.Flows.GroupBy(f => f.Program).ToDictionary(g => g.Key, g => g.ToArray());
        if (flows.Count != 5 || flows.Any(p => p.Value.Length != 1)) return new(new(new[] { new CanonicalExecutionGraphFact("MissingOrDuplicateStreamProgram", contract.Anchor) }), default);
        var sourceName = contract.SourceKind == StreamSourceKind.Handler ? "handler" : "wrapper";
        var sourceFactory = contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.SourceFactory : StreamPhase.WrapperFactory;
        var sourceResolve = contract.SourceKind == StreamSourceKind.Handler ? StreamPhase.ResolveHandler : StreamPhase.ResolveWrapper;
        var initialValues = ImmutableDictionary<string, string?>.Empty.Add("api/request", "request:enumeration").Add("api/token", "token:api")
            .Add("enumerator/token", "token:enumerator").Add("CancellationToken.None", "token:none");
        foreach (var linked in new[] { false, true })
        {
            var created = Run(StreamProgram.Create, new(initialValues, ImmutableArray<int>.Empty), linked).FirstOrDefault();
            if (created == null) continue;
            var fresh = Run(StreamProgram.GetEnumerator, created, linked).FirstOrDefault();
            if (fresh == null) continue;
            var pending = new Queue<Life>(); var seen = new HashSet<string>(); pending.Enqueue(fresh);
            while (pending.Count != 0)
            {
                var life = pending.Dequeue(); if (!seen.Add(LifeKey(life))) continue;
                foreach (var program in new[] { StreamProgram.Move, StreamProgram.Current, StreamProgram.Dispose })
                    foreach (var end in Run(program, life, linked)) if (LifeKey(end) != LifeKey(life)) pending.Enqueue(end);
            }
        }
        return new(new(facts.OrderBy(f => f.Reason, StringComparer.Ordinal)), new(paths));

        IEnumerable<Life> Run(StreamProgram program, Life initial, bool linked)
        {
            var flow = flows[program][0]; var edges = flow.Edges.ToLookup(e => e.From);
            var todo = new Queue<State>(); var seen = new HashSet<string>(); var ends = new Dictionary<string, Life>();
            var expected = initial.Acquired.Where(i => Sessions(contract, i)[0].Ownership == FusionOwnership.OwnedSession && Read(initial, Session(i) + "/live") != null).ToImmutableArray();
            todo.Enqueue(new(flow.Entry, initial, ImmutableHashSet<int>.Empty, ImmutableHashSet<int>.Empty, expected,
                ImmutableArray<int>.Empty, ImmutableArray<StreamSiteId>.Empty, ImmutableArray<string>.Empty));
            while (todo.Count != 0)
            {
                var state = todo.Dequeue(); if (!seen.Add(Key(state))) continue;
                var id = state.At; var phase = id.Phase; var life = state.Life;
                var visited = state.Visited.Add(id); state = state with { Visited = visited };
                if (Terminal(phase))
                {
                    var startupFailure = program == StreamProgram.Move && !life.Started;
                    if (startupFailure || state.DisposeBegan)
                    {
                        if (positions.Any(i => Read(life, Session(i) + "/live") != null)) Reject("StreamSessionStillLiveAfterCleanup");
                        if (!state.Released.SequenceEqual(state.Expected.Reverse())) Reject("StreamSessionsNotReleasedExactlyOnceInReverseOrder");
                    }
                    if (program == StreamProgram.Move && life.Started && !life.Disposed && positions.Any(i => Read(life, Session(i) + "/live") != Session(i) + "/instance")) Reject("StartedMoveReleasedSessionBeforeDispose");
                    if (startupFailure && (Read(life, "cursor/source") != null || Read(life, "cursor/linked-token") != null)) Reject("StartupFailureLeakedPartialOwner");
                    if (phase == StreamPhase.MoveFailure && state.Error == null || phase == StreamPhase.DisposeSuccess && state.Error != null ||
                        phase == StreamPhase.DisposeFailure && state.Error == null) Reject("StreamSessionExitErasedFailure");
                    paths.Add(new(program, phase, life.Started, new(life.Acquired), new(positions.Where(i => Read(life, Session(i) + "/live") != null)),
                        new(state.Released), new(visited), new(state.Failures), state.Error, state.Error?.StartsWith("cancel:", StringComparison.Ordinal) == true));
                    ends[LifeKey(life)] = life;
                    continue;
                }
                foreach (var completion in Outcomes(state))
                {
                    var next = state; var invalid = false;
                    void Need(bool value, string reason) { if (!value) { Reject(reason); invalid = true; } }
                    var outcome = completion.Outcome; var failed = outcome == StreamOutcome.Failure;
                    var values = life.Values; var resource = Session(id.Stage);
                    var sessionPhase = phase is StreamPhase.SessionGuard or StreamPhase.ResolveSessionFactory or StreamPhase.AcquireSession or
                        StreamPhase.ValidateSessionResult or StreamPhase.CommitSession or StreamPhase.DetachSession or StreamPhase.SessionPresent or StreamPhase.CleanupSession;
                    if (sessionPhase && !positions.Contains(id.Stage)) { Reject("UnknownStreamSessionPosition"); continue; }
                    if (phase is StreamPhase.SessionGuard or StreamPhase.ResolveSessionFactory or StreamPhase.AcquireSession)
                        Need(program == StreamProgram.Move && !life.Started && !life.Ended && !life.Disposed && state.PreCancel && state.Error == null, "SessionAcquisitionOutsideLazyStartup");
                    if (phase == StreamPhase.SessionGuard && !failed) next = next with { Guarded = next.Guarded.Add(id.Stage) };
                    if (phase == StreamPhase.ResolveSessionFactory || phase == sourceResolve && contract.SourceSessions.Count != 0)
                    {
                        if (phase == StreamPhase.ResolveSessionFactory) Need(next.Guarded.Contains(id.Stage), "SessionFactoryResolveBeforeGuard");
                        var resolution = candidate.ServiceResolutions.Where(r => r.At == id).ToArray();
                        Need(resolution.Length == 1 && resolution[0].ServiceType == Sessions(contract, id.Stage)[0].FactoryServiceType &&
                            resolution[0].ResultSlot == Service(id.Stage) + "/resolved", "StreamSessionFactoryDITypeOrSlotMismatch");
                        if (!failed && resolution.Length == 1) values = values.SetItem(resolution[0].ResultSlot, "factory:" + id.Stage);
                    }
                    if (phase == StreamPhase.AcquireSession)
                    {
                        var call = Call(id); CheckAwait(id, Sessions(contract, id.Stage)[0].Acquire, false);
                        Need(next.Guarded.Contains(id.Stage) && call != null && call.Receiver == Service(id.Stage) + "/resolved" && Read(life, call.Receiver) == "factory:" + id.Stage &&
                            call.Result == resource + "/factory-result", "StreamFactoryCallReceiverOrResultMismatch");
                        Need(call != null && call.RequestSlot == "cursor/request" && call.TokenSlot == "cursor/start-token" &&
                            Read(life, "cursor/request") == "request:enumeration" && Read(life, "cursor/start-token") == (linked ? "token:linked" : "token:api"), "StreamFactoryRequestOrTokenMismatch");
                        if (!failed && call?.Result != null) values = values.SetItem(call.Result, completion.Null ? null : resource + "/instance");
                    }
                    if (phase == StreamPhase.ValidateSessionResult && !failed) next = next with { Validated = next.Validated.Add(id.Stage) };
                    if (phase == StreamPhase.CommitSession)
                    {
                        Need(next.Validated.Contains(id.Stage) && Read(life, resource + "/factory-result") == resource + "/instance" && state.Error == null,
                            "OwnershipTransferredBeforeNonNullSessionValidation");
                        var sequence = Enumerable.Reverse(positions).ToArray();
                        Need(life.Acquired.SequenceEqual(sequence.Take(life.Acquired.Length)) && life.Acquired.Length < sequence.Length && sequence[life.Acquired.Length] == id.Stage,
                            "StreamSessionStartupOrderMismatch");
                        next = next with
                        {
                            Life = next.Life with
                            {
                                Acquired = next.Life.Acquired.Add(id.Stage),
                                HookCache = id.Stage < 0 ? next.Life.HookCache : next.Life.HookCache + "|" + id.Stage + "|"
                            }
                        };
                        if (Sessions(contract, id.Stage)[0].Ownership == FusionOwnership.OwnedSession) next = next with { Expected = next.Expected.Add(id.Stage) };
                    }
                    if (phase == sourceFactory || phase is StreamPhase.Filter or StreamPhase.Map)
                    {
                        var position = phase == sourceFactory ? -1 : id.Stage;
                        if (positions.Contains(position))
                        {
                            Need(state.Error == null && !life.Ended && !life.Disposed &&
                                (phase == sourceFactory ? !life.Started && state.PreCancel : life.Started), "StreamSessionCallbackOutsideLiveBody");
                            var receiver = candidate.CallbackReceivers.Where(c => c.At == id).ToArray();
                            Need(receiver.Length == 1 && receiver[0].Receiver == Session(position) + "/live" && Read(life, receiver[0].Receiver) == Session(position) + "/instance",
                                "StreamBodyReceiverIsNotLiveTransferredSession");
                            Need(receiver.Length == 1 && receiver[0].RequestSlot == "cursor/request" && receiver[0].TokenSlot == "cursor/start-token" &&
                                Read(life, "cursor/request") == "request:enumeration" && Read(life, "cursor/start-token") == (linked ? "token:linked" : "token:api"), "StreamBodyRequestOrTokenMismatch");
                        }
                    }
                    if (phase is StreamPhase.SourceMove or StreamPhase.DisposeSource)
                    {
                        var awaiters = candidate.Awaits.Where(a => a.Producer == id).ToArray();
                        Need(awaiters.Length == 1 && positions.All(i => awaiters[0].RetainedSlots.Contains(Session(i) + "/live") && awaiters[0].RetainedSlots.Contains(Session(i) + "/detached")),
                            "StreamSessionLeaseLostAcrossPendingAwait");
                    }
                    if (phase == StreamPhase.DetachSession)
                        Need(program == StreamProgram.Dispose && state.DisposeBegan || program == StreamProgram.Move && !life.Started && state.Error != null, "StreamSessionDetachOutsideCleanupBoundary");
                    if (phase == StreamPhase.CleanupSession || phase == StreamPhase.DisposeSource || phase is StreamPhase.StartupDisposeLink or StreamPhase.DisposeLink)
                        Need(positions.All(i => Read(life, Session(i) + "/live") == null) && Read(life, "cursor/source") == null && Read(life, "cursor/linked-token") == null,
                            "StreamUserCleanupBeforeAllOwnersDetached");
                    if (phase == StreamPhase.CleanupSession)
                    {
                        var session = Sessions(contract, id.Stage)[0]; var call = Call(id);
                        Need(session.Ownership == FusionOwnership.OwnedSession, "BorrowedStreamSessionDisposedByOperation");
                        Need(call != null && call.Receiver == resource + "/detached" && Read(life, call.Receiver) == resource + "/instance", "StreamCleanupReceiverIsNotTransferredSession");
                        Need(state.Expected.Reverse().Where(i => !state.Released.Contains(i)).DefaultIfEmpty(int.MinValue).First() == id.Stage, "StreamSessionCleanupOrderOrDuplicateMismatch");
                        Need(program != StreamProgram.Dispose || Read(life, "dispose/source") == null || state.SourceCleaned, "StreamSessionCleanupBeforeSourceCompleted");
                        if (session.Cleanup != null) CheckAwait(id, session.Cleanup, true);
                        next = next with { Released = next.Released.Add(id.Stage) };
                    }
                    if (phase == StreamPhase.DisposeSource)
                    {
                        Need(Read(life, "dispose/source") == "source-instance" && !state.SourceCleaned, "StreamSourceCleanupWithoutDetachedOwnerOrRepeated");
                        next = next with { SourceCleaned = true };
                    }
                    if (phase is StreamPhase.StartupDisposeLink or StreamPhase.DisposeLink)
                    {
                        Need(state.Released.SequenceEqual(state.Expected.Reverse()) && (program != StreamProgram.Dispose || Read(life, "dispose/source") == null || state.SourceCleaned),
                            "StreamLinkedTokenCleanupBeforeSourceAndSessionsCompleted");
                        Need(Read(life, program == StreamProgram.Move ? "startup/linked-token" : "dispose/linked-token") == "link-instance" && !state.LinkCleaned, "StreamLinkCleanupWithoutDetachedOwnerOrRepeated");
                        next = next with { LinkCleaned = true };
                    }
                    switch (phase)
                    {
                        case StreamPhase.MergeTokens:
                            if (!failed) values = values.SetItem("token-selection/result", linked ? "token:linked" : "token:api").SetItem("cursor/linked-token", linked ? "link-instance" : null);
                            break;
                        case StreamPhase.PreCancel: next = next with { PreCancel = !failed }; break;
                        case StreamPhase.ResolveHook:
                            if (!failed) next = next with { Life = next.Life with { HookCache = next.Life.HookCache + "|" + id.Stage + "|" } };
                            break;
                        case StreamPhase.SourceFactory:
                        case StreamPhase.WrapperFactory: if (!failed) values = values.SetItem(sourceName + "/enumerable", "source-enumerable"); break;
                        case StreamPhase.AcquireSource: if (!failed) values = values.SetItem(sourceName + "/enumerable", "source-instance"); break;
                        case StreamPhase.MarkStarted:
                            Need(positions.All(i => Read(life, Session(i) + "/live") == Session(i) + "/instance"), "StreamStartedBeforeSessionTransfers");
                            next = next with { Life = next.Life with { Started = true } }; break;
                        case StreamPhase.ClearCurrent: next = next with { Life = next.Life with { Cache = false }, Busy = true }; break;
                        case StreamPhase.CacheCurrent: next = next with { Life = next.Life with { Cache = true } }; break;
                        case StreamPhase.EndFalse:
                        case StreamPhase.EndFailure: next = next with { Life = next.Life with { Ended = true, Cache = false } }; break;
                        case StreamPhase.MarkDisposed: next = next with { Life = next.Life with { Disposed = true, Cache = false }, DisposeBegan = true, Busy = true }; break;
                        case StreamPhase.ReleaseTrue:
                        case StreamPhase.ReleaseFalse:
                        case StreamPhase.ReleaseFailure:
                        case StreamPhase.ReleaseDispose: next = next with { Busy = false }; break;
                    }
                    if (failed)
                    {
                        var error = (completion.Canceled ? "cancel:" : "fault:") + id;
                        var policy = candidate.Failures.Where(f => f.At == id).ToArray();
                        // Guard misuse is handled by the base lifecycle validator, never an owner cleanup exception.
                        if (phase is not (StreamPhase.NullGuard or StreamPhase.MoveGuard or StreamPhase.DisposeGuard))
                            Need(policy.Length == 1 && policy[0].PreserveIdentityAndToken && policy[0].EscapingOceIsCanceled && policy[0].ReplacesPreviousError && !policy[0].Aggregate, "StreamSessionExceptionReplacementMismatch");
                        next = next with { Error = error, Failures = next.Failures.Add(error) };
                        values = values.SetItem("producer/error", error);
                    }
                    foreach (var transfer in candidate.Transfers.Where(t => t.At == id && (t.Outcome == StreamOutcome.Always || t.Outcome == outcome)))
                        values = values.SetItem(transfer.Destination, transfer.Source == "null" ? null : values.TryGetValue(transfer.Source, out var value) ? value : null);
                    next = next with { Life = next.Life with { Values = values } };
                    if (failed && phase is not (StreamPhase.NullGuard or StreamPhase.MoveGuard or StreamPhase.DisposeGuard))
                        Need(Read(next.Life, "cursor/error") == next.Error, "StreamSessionExceptionIdentityOrTokenLost");
                    if (phase == StreamPhase.CommitSession)
                        Need(Read(next.Life, resource + "/live") == resource + "/instance" && Read(next.Life, resource + "/factory-result") == null, "StreamSessionTransferLostOrAliasedResource");
                    if (phase == StreamPhase.DetachSession)
                        Need(Read(next.Life, resource + "/live") == null && Read(next.Life, resource + "/detached") == Read(life, resource + "/live"), "StreamSessionDetachLostOrRetainedLiveAlias");
                    var choices = edges[id].Where(e => e.Outcome == outcome).ToArray();
                    Need(choices.Length == 1, "StreamSessionPathMissingOrAmbiguousOutcome");
                    if (!invalid) todo.Enqueue(next with { At = choices[0].To });
                }
            }
            return ends.Values;
        }
        StreamSessionCall? Call(StreamSiteId id)
        {
            var found = candidate.SessionCalls.Where(c => c.At == id).ToArray(); return found.Length == 1 ? found[0] : null;
        }
        void CheckAwait(StreamSiteId id, BoundCallbackModel callback, bool cleanup)
        {
            var sites = candidate.Sites.Where(s => s.Id == id).ToArray();
            if (sites.Length != 1 || sites[0].Producer != callback.Implementation || sites[0].Completion != callback.Completion) Reject("StreamSessionCallbackBindingMismatch");
            var awaiters = candidate.Awaits.Where(a => a.Producer == id).ToArray();
            if (callback.Completion != BoundCompletionKind.ValueTask)
            {
                if (awaiters.Length != 0) Reject("SynchronousStreamSessionCallbackAwaited"); return;
            }
            if (awaiters.Length != 1 || !awaiters[0].ConsumeOnce || !awaiters[0].ConfigureAwaitFalse || awaiters[0].ResultType != (cleanup ? "void" : Sessions(contract, id.Stage)[0].ResourceType) ||
                awaiters[0].Normal != (cleanup ? "continue-session-cleanup" : "transfer-session")) Reject("StreamSessionAwaitConsumptionOrContinuationMismatch");
            else if (positions.Any(i => !awaiters[0].RetainedSlots.Contains(Session(i) + "/live") || !awaiters[0].RetainedSlots.Contains(Session(i) + "/detached"))) Reject("StreamSessionLeaseLostAcrossPendingAwait");
            else if (!awaiters[0].RetainedSlots.Contains(Service(id.Stage) + "/resolved")) Reject("StreamSessionFactoryLostAcrossPendingAwait");
        }
        IEnumerable<(StreamOutcome Outcome, bool Canceled, bool Null)> Outcomes(State state)
        {
            var id = state.At; var life = state.Life;
            bool? decision = id.Phase switch
            {
                StreamPhase.Stopped => life.Ended || life.Disposed,
                StreamPhase.Started => life.Started,
                StreamPhase.HookCached => life.HookCache.Contains("|" + id.Stage + "|"),
                StreamPhase.SourcePresent => Read(life, "dispose/source") != null,
                StreamPhase.SessionPresent => Read(life, Session(id.Stage) + "/detached") != null,
                StreamPhase.StartupLinkPresent => Read(life, "startup/linked-token") != null,
                StreamPhase.LinkPresent => Read(life, "dispose/linked-token") != null,
                StreamPhase.AlreadyDisposed => life.Disposed,
                StreamPhase.DisposeResult => state.Error != null,
                StreamPhase.CurrentGuard => life.Cache && !life.Ended && !life.Disposed,
                _ => null
            };
            if (decision != null) { yield return (decision.Value ? StreamOutcome.True : StreamOutcome.False, false, false); yield break; }
            if (id.Phase == StreamPhase.ValidateSessionResult) { yield return (Read(life, Session(id.Stage) + "/factory-result") == null ? StreamOutcome.Failure : StreamOutcome.Success, false, false); yield break; }
            if (id.Phase is StreamPhase.SourceMove or StreamPhase.Filter)
            {
                yield return (StreamOutcome.True, false, false); yield return (StreamOutcome.False, false, false);
                yield return (StreamOutcome.Failure, false, false); yield return (StreamOutcome.Failure, true, false); yield break;
            }
            if (id.Phase is StreamPhase.NullGuard or StreamPhase.MoveGuard or StreamPhase.DisposeGuard) { yield return (StreamOutcome.Success, false, false); yield break; }
            if (candidate.Failures.Any(f => f.At == id))
            {
                yield return (StreamOutcome.Success, false, false);
                if (id.Phase == StreamPhase.AcquireSession) yield return (StreamOutcome.Success, false, true);
                yield return (StreamOutcome.Failure, false, false); yield return (StreamOutcome.Failure, true, false); yield break;
            }
            yield return (StreamOutcome.Always, false, false);
        }
    }
    private static string? Read(Life life, string slot) => life.Values.TryGetValue(slot, out var value) ? value : null;
    private static string LifeKey(Life life) => life.Started + ":" + life.Ended + ":" + life.Disposed + ":" + life.Cache + ":" + life.HookCache + ":" + string.Join(",", life.Acquired) + ":" +
        string.Join(";", life.Values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => v.Key + "=" + v.Value));
    private static string Key(State s) => s.At + ":" + LifeKey(s.Life) + ":" + s.Error + ":" + s.Busy + ":" + s.PreCancel + ":" + s.DisposeBegan + ":" + s.SourceCleaned + ":" + s.LinkCleaned + ":" +
        string.Join(",", s.Guarded.OrderBy(i => i)) + ":" + string.Join(",", s.Validated.OrderBy(i => i)) + ":" + string.Join(",", s.Expected) + ":" + string.Join(",", s.Released) + ":" + string.Join(";", s.Failures);
    private static bool Terminal(StreamPhase phase) => phase is StreamPhase.NullFailure or StreamPhase.EnvelopeReturn or StreamPhase.CursorReturn or StreamPhase.MoveTrue or
        StreamPhase.MoveFalse or StreamPhase.MoveFailure or StreamPhase.BusyFailure or StreamPhase.ReadCache or StreamPhase.CurrentFailure or StreamPhase.DisposeSuccess or StreamPhase.DisposeFailure;
}
