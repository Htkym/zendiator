using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.SourceGenerator;

internal sealed record SendSessionPathEvidence(string Owner, EquatableArray<int> Acquired,
    EquatableArray<int> CleanupOrder, EquatableArray<SendExecutionSiteId> Visited,
    EquatableArray<string> FailureOrder, string? EscapingError, bool Canceled);
internal sealed record SendSessionInspection(EquatableArray<CanonicalExecutionGraphFact> Facts, EquatableArray<SendSessionPathEvidence> Paths);

/// <summary>
/// Executes candidate operands and conditional paths against D03/D04 ownership obligations.
/// It never calls a builder or RequiredSend*. Callee outcomes summarize separately validated inner scopes.
/// No arbitrary factory or wrapper body is analyzed; exclusive transfer is an explicit caller contract.
/// </summary>
internal static class CanonicalSendSessionValidator
{
    private sealed record State(SendControlPoint Point, ImmutableDictionary<string, string?> Values,
        ImmutableHashSet<int> Guarded, ImmutableHashSet<int> Acquired, ImmutableArray<int> Released,
        ImmutableArray<SendExecutionSiteId> Visited, ImmutableArray<string> Failures, bool Entered, string? Error);

    internal static EquatableArray<CanonicalExecutionGraphFact> Validate(BoundPipelineModel route, CanonicalSendExecutionSchedule schedule) => Inspect(route, schedule).Facts;

    internal static SendSessionInspection Inspect(BoundPipelineModel route, CanonicalSendExecutionSchedule schedule)
    {
        var facts = new HashSet<CanonicalExecutionGraphFact>(); var evidence = new List<SendSessionPathEvidence>();
        void Reject(string reason) => facts.Add(new(reason, route.Anchor));
        var calls = schedule.SessionCalls.GroupBy(c => c.At).ToDictionary(g => g.Key, g => g.ToArray());
        if (calls.Values.Any(v => v.Length != 1)) Reject("DuplicateSessionCall");
        if (schedule.SessionTransfers.Any(t => t.At.Phase is not (SendExecutionPhase.CommitSession or SendExecutionPhase.DetachSession))) Reject("SessionTransferOutsideOwnershipBoundary");
        for (var branch = 0; branch < route.Handlers.Count; branch++)
            for (var stage = 0; stage <= route.Stages.Count; stage++)
            {
                var declared = Sessions(route, branch, stage); if (declared.Count == 0) continue;
                var owner = Scope(branch, stage);
                var matches = schedule.ControlFlows.Where(f => f.Owner == owner).ToArray();
                if (matches.Length != 1) { Reject("SessionControlFlowRequired"); continue; }
                var edges = matches[0].Edges.ToLookup(e => e.From);
                string Resource(int index) => Session(branch, stage, index);
                    var work = new Stack<State>();
                    work.Push(new(Entry(owner), ImmutableDictionary<string, string?>.Empty,
                        ImmutableHashSet<int>.Empty, ImmutableHashSet<int>.Empty, ImmutableArray<int>.Empty,
                        ImmutableArray<SendExecutionSiteId>.Empty, ImmutableArray<string>.Empty, false, null));
                    while (work.Count != 0)
                    {
                        var state = work.Pop(); var id = state.Point.Site;
                        if (state.Point.Terminal is SendControlTerminal.Success or SendControlTerminal.Failure)
                        {
                            if ((state.Point.Terminal == SendControlTerminal.Success) != (state.Error == null)) Reject("SessionExitErasedFailure");
                            var expected = state.Acquired.Where(i => declared[i].Ownership == FusionOwnership.OwnedSession).OrderByDescending(i => i).ToArray();
                            if (!state.Released.SequenceEqual(expected)) Reject("AcquiredSessionsNotReleasedExactlyOnceInReverseOrder");
                            if (Enumerable.Range(0, declared.Count).Any(i => Read(state, Resource(i) + "/live") != null)) Reject("SessionStillLiveAtScopeExit");
                            evidence.Add(new(owner, new(state.Acquired.OrderBy(i => i)), new(state.Released), new(state.Visited), new(state.Failures), state.Error,
                                schedule.EscapingOceIsCanceled && state.Error?.StartsWith("cancel:", StringComparison.Ordinal) == true));
                            continue;
                        }
                        if (id != null)
                        {
                            if (state.Visited.Contains(id))
                            {
                                Reject(id.Phase == SendExecutionPhase.SessionGuard && state.Released.Length != 0
                                    ? "CancellationGuardReachedDuringSessionCleanup" : "SessionControlCycleOrRepeatedRelease");
                                continue;
                            }
                            state = state with { Visited = state.Visited.Add(id) };
                            var index = id.Session;
                            var sessionPhase = id.Phase is SendExecutionPhase.SessionGuard or SendExecutionPhase.ResolveSessionFactory or SendExecutionPhase.AcquireSession or
                                SendExecutionPhase.CommitSession or SendExecutionPhase.DetachSession or SendExecutionPhase.SessionPresent or SendExecutionPhase.CleanupSession;
                            if (sessionPhase && (index < 0 || index >= declared.Count)) { Reject("UnknownSessionIndex"); continue; }
                            switch (id.Phase)
                            {
                                case SendExecutionPhase.SessionGuard:
                                    if (state.Entered || state.Error != null || state.Acquired.Count != index || !state.Visited.Contains(Site(branch, stage, SendExecutionPhase.Resolve))) Reject("SessionGuardNotAtAcquisitionBoundary");
                                    break;
                                case SendExecutionPhase.ResolveSessionFactory:
                                    if (!state.Guarded.Contains(index) || state.Error != null) Reject("FactoryResolutionBeforeSessionGuard");
                                    break;
                                case SendExecutionPhase.AcquireSession:
                                    if (Read(state, Resource(index) + "/factory/resolved") == null || state.Error != null) Reject("SessionAcquiredBeforeFactoryResolutionOrAfterFailure");
                                    CheckCall(id, declared[index].Acquire, false);
                                    break;
                                case SendExecutionPhase.CommitSession:
                                    if (!state.Acquired.Contains(index) || state.Error != null) Reject("OwnershipTransferredBeforeSuccessfulFactoryCompletion");
                                    state = Transfer(state, id);
                                    if (Read(state, Resource(index) + "/live") != Resource(index) + "/instance") Reject("SessionTransferLostOrAliasedResource");
                                    break;
                                case SendExecutionPhase.Entered:
                                    if (state.Acquired.Count != declared.Count || Enumerable.Range(0, declared.Count).Any(i => Read(state, Resource(i) + "/live") != Resource(i) + "/instance")) Reject("EnteredBeforeSessionTransfer");
                                    state = state with { Entered = true };
                                    break;
                                case SendExecutionPhase.DetachSession:
                                    state = Transfer(state, id);
                                    if (Read(state, Resource(index) + "/live") != null || Read(state, Resource(index) + "/detached") !=
                                        (state.Acquired.Contains(index) ? Resource(index) + "/instance" : null)) Reject("SessionDetachedWithoutClearingExactLiveLease");
                                    break;
                                case SendExecutionPhase.CleanupSession:
                                    if (declared[index].Ownership != FusionOwnership.OwnedSession) Reject("BorrowedResourceDisposedByOperation");
                                    if (Enumerable.Range(0, declared.Count).Any(i => Read(state, Resource(i) + "/live") != null)) Reject("UserCleanupBeforeAllLeasesDetached");
                                    var next = state.Acquired.Where(i => declared[i].Ownership == FusionOwnership.OwnedSession && !state.Released.Contains(i)).OrderByDescending(i => i).DefaultIfEmpty(-1).First();
                                    if (next != index) Reject("SessionCleanupOrderOrDuplicateMismatch");
                                    if (state.Entered && stage < route.Stages.Count && route.Stages[stage].Callbacks.Any(c => c.Phase == BoundCallbackPhase.Finally) &&
                                        !state.Visited.Contains(Site(branch, stage, SendExecutionPhase.Finally))) Reject("OwnedCleanupBeforeHookFinally");
                                    var call = Call(id);
                                    if (call == null || Read(state, call.Receiver) != Resource(index) + "/instance") Reject("CleanupReceiverIsNotTransferredSession");
                                    if (declared[index].Cleanup != null) CheckCall(id, declared[index].Cleanup!, true);
                                    state = state with { Released = state.Released.Add(index) };
                                    break;
                            }
                        }
                        var pure = id == null || id.Phase is SendExecutionPhase.Entered or SendExecutionPhase.CommitSession or SendExecutionPhase.DetachSession or
                            SendExecutionPhase.SessionPresent or SendExecutionPhase.SessionResult;
                        foreach (var completion in pure ? new[] { (Outcome: SendControlOutcome.Always, Canceled: false) } :
                            new[] { (Outcome: SendControlOutcome.Success, Canceled: false), (Outcome: SendControlOutcome.Failure, Canceled: false), (Outcome: SendControlOutcome.Failure, Canceled: true) })
                        {
                            var outcome = completion.Outcome;
                            var next = state;
                            if (id != null && outcome == SendControlOutcome.Success)
                            {
                                var resource = Resource(id.Session);
                                if (id.Phase == SendExecutionPhase.SessionGuard) next = next with { Guarded = next.Guarded.Add(id.Session) };
                                if (id.Phase == SendExecutionPhase.ResolveSessionFactory) next = next with { Values = next.Values.SetItem(resource + "/factory/resolved", "borrowed-factory:" + resource) };
                                if (id.Phase == SendExecutionPhase.AcquireSession)
                                {
                                    var call = Call(id);
                                    if (call?.Result == null || Read(next, call.Receiver) != "borrowed-factory:" + resource) Reject("FactoryCallReceiverOrResultMismatch");
                                    else next = next with { Values = next.Values.SetItem(call.Result, resource + "/instance") };
                                    next = next with { Acquired = next.Acquired.Add(id.Session) };
                                }
                            }
                            if (outcome == SendControlOutcome.Failure)
                            {
                                var error = (completion.Canceled ? "cancel:" : "fault:") + id;
                                var call = id == null ? null : Call(id);
                                // Policies on actual calls specify natural replacement, never aggregation or reconstruction.
                                if (call != null && (!call.PreserveIdentityAndToken || !call.ReplacePreviousError || call.Aggregate)) Reject("SessionExceptionReplacementIdentityMismatch");
                                next = next with { Error = error, Failures = next.Failures.Add(error) };
                            }
                            var present = id != null && Read(next, Resource(id.Session) + "/detached") != null;
                            var selected = edges[state.Point].Where(e => e.Outcome == outcome && (e.Predicate switch
                            {
                                SendControlPredicate.Any => true,
                                SendControlPredicate.NoFailure => next.Error == null,
                                SendControlPredicate.HasFailure => next.Error != null,
                                SendControlPredicate.SessionPresent => present,
                                SendControlPredicate.SessionAbsent => !present,
                                _ => false
                            })).ToArray();
                            if (selected.Length != 1) Reject("SessionPathMissingOrAmbiguousOutcome");
                            else work.Push(next with { Point = selected[0].To });
                        }
                    }
                State Transfer(State state, SendExecutionSiteId id)
                {
                    foreach (var transfer in schedule.SessionTransfers.Where(t => t.At == id))
                    {
                        if (!transfer.Destination.StartsWith(owner + "/session:", StringComparison.Ordinal)) Reject("SessionSlotEscapedInvocation");
                        var value = transfer.Source == "null" ? null : Read(state, transfer.Source);
                        state = state with { Values = state.Values.SetItem(transfer.Destination, value) };
                    }
                    return state;
                }
                SendSessionCall? Call(SendExecutionSiteId id) => calls.TryGetValue(id, out var found) && found.Length == 1 ? found[0] : null;
                void CheckCall(SendExecutionSiteId id, BoundCallbackModel callback, bool cleanup)
                {
                    var sites = schedule.Sites.Where(s => s.Id == id).ToArray();
                    if (sites.Length != 1 || sites[0].Producer != callback.Implementation || sites[0].Completion != callback.Completion) Reject("SessionCallbackBindingMismatch");
                    var awaits = schedule.Awaits.Where(a => a.Producer == id).ToArray();
                    if (callback.Completion == BoundCompletionKind.ValueTask)
                    {
                        if (awaits.Length != 1 || !awaits[0].ConsumeOnce || !awaits[0].ConfigureAwaitFalse || awaits[0].Normal !=
                            (cleanup ? SendAwaitContinuation.ContinueSessionCleanup : SendAwaitContinuation.TransferSession)) Reject("SessionAwaitConsumptionOrContinuationMismatch");
                        else if (awaits[0].ResultType != (cleanup ? "void" : declared[id.Session].ResourceType)) Reject("SessionAwaitResultMismatch");
                        else if (Enumerable.Range(0, declared.Count).Any(i => !awaits[0].RetainedSlots.Contains(Resource(i) + "/live") || !awaits[0].RetainedSlots.Contains(Resource(i) + "/detached"))) Reject("SessionLeaseLostAcrossPendingAwait");
                    }
                    else if (awaits.Length != 0) Reject("SynchronousSessionCallbackAwaited");
                }
            }
        return new(new(facts.OrderBy(f => f.Reason, StringComparer.Ordinal)), new(evidence));
    }

    private static string? Read(State state, string slot) => state.Values.TryGetValue(slot, out var value) ? value : null;
}
