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
        ImmutableArray<SendExecutionSiteId> Visited, ImmutableArray<string> Failures, bool Entered, string? Error)
    {
        public ImmutableHashSet<SendExecutionPhase> CompletedCallbacks { get; init; } = ImmutableHashSet<SendExecutionPhase>.Empty;
        public ImmutableHashSet<SendExecutionPhase> SuccessfulCallbacks { get; init; } = ImmutableHashSet<SendExecutionPhase>.Empty;
        public SendExecutionSiteId? FirstFailure { get; init; }
        public bool CleanupStarted { get; init; }
        public ImmutableHashSet<int> ValidatedSessions { get; init; } = ImmutableHashSet<int>.Empty;
    }

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
                var callbackSession = declared.Count == 1 && declared[0].SuppliesCallbackReceiver;
                var invocationState = schedule.States.SingleOrDefault(st => st.Owner == owner);
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
                            SendExecutionPhase.ValidateSessionResult or SendExecutionPhase.CommitSession or SendExecutionPhase.DetachSession or SendExecutionPhase.SessionPresent or SendExecutionPhase.CleanupSession;
                        if (sessionPhase && (index < 0 || index >= declared.Count)) { Reject("UnknownSessionIndex"); continue; }
                        if (IsBodyCallback(id.Phase))
                        {
                            if (!state.Entered) Reject("CallbackReachedBeforeSessionScopeEntered");
                            if (state.CleanupStarted) Reject("SessionBodyCallbackAfterDetach");
                            if (callbackSession && id.Phase != SendExecutionPhase.InvokeInner)
                            {
                                var receiver = schedule.CallbackReceivers.Where(c => c.At == id).ToArray();
                                if (receiver.Length != 1 || receiver[0].Receiver != Resource(0) + "/live" ||
                                    Read(state, receiver[0].Receiver) != Resource(0) + "/instance") Reject("BodyReceiverIsNotLiveTransferredSession");
                                else if (invocationState == null || receiver[0].TokenSlot != invocationState.StartTokenSlot ||
                                    receiver[0].RequestSlot != (id.Phase == SendExecutionPhase.Before || invocationState.EffectiveRequestSlot == null
                                        ? invocationState.InputRequestSlot : invocationState.EffectiveRequestSlot)) Reject("BodySessionRequestOrTokenSlotMismatch");
                                if (schedule.Awaits.Where(a => a.Producer == id).Any(a =>
                                    !a.RetainedSlots.Contains(Resource(0) + "/live") || !a.RetainedSlots.Contains(Resource(0) + "/detached")))
                                    Reject("SessionLeaseLostAcrossPendingAwait");
                            }
                        }
                        if (id.Phase is SendExecutionPhase.DetachSession or SendExecutionPhase.SessionPresent or SendExecutionPhase.CleanupSession)
                        {
                            if (!state.CleanupStarted && !ReadyForCleanup(state)) Reject("SessionCleanupStartedBeforeBodyOutcome");
                            state = state with { CleanupStarted = true };
                        }
                        switch (id.Phase)
                        {
                            case SendExecutionPhase.SessionGuard:
                                if (state.Entered || state.Error != null || state.Acquired.Count != index || !state.Visited.Contains(Site(branch, stage, SendExecutionPhase.Resolve))) Reject("SessionGuardNotAtAcquisitionBoundary");
                                break;
                            case SendExecutionPhase.ResolveSessionFactory:
                                if (!state.Guarded.Contains(index) || state.Error != null) Reject("FactoryResolutionBeforeSessionGuard");
                                break;
                            case SendExecutionPhase.AcquireSession:
                                if (Read(state, callbackSession ? Service(branch, stage) + "/resolved" : Resource(index) + "/factory/resolved") == null || state.Error != null) Reject("SessionAcquiredBeforeFactoryResolutionOrAfterFailure");
                                if (!state.Guarded.Contains(index)) Reject("SessionAcquiredBeforeCancellationGuard");
                                CheckCall(id, declared[index].Acquire, false);
                                var acquisition = Call(id);
                                if (invocationState == null || acquisition == null || acquisition.RequestSlot != invocationState.InputRequestSlot || acquisition.TokenSlot != invocationState.StartTokenSlot)
                                    Reject("SessionFactoryRequestOrTokenSlotMismatch");
                                break;
                            case SendExecutionPhase.CommitSession:
                                if (callbackSession)
                                {
                                    if (!state.ValidatedSessions.Contains(index) || Read(state, Resource(index) + "/factory-result") != Resource(index) + "/instance" || state.Error != null)
                                        Reject("OwnershipTransferredBeforeNonNullSessionValidation");
                                    else state = state with { Acquired = state.Acquired.Add(index) };
                                }
                                else if (!state.Acquired.Contains(index) || state.Error != null) Reject("OwnershipTransferredBeforeSuccessfulFactoryCompletion");
                                state = Transfer(state, id);
                                if (Read(state, Resource(index) + "/live") != Resource(index) + "/instance") Reject("SessionTransferLostOrAliasedResource");
                                if (callbackSession && Read(state, Resource(index) + "/factory-result") != null) Reject("FactoryResultAliasRetainedAfterTransfer");
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
                                    !state.CompletedCallbacks.Contains(SendExecutionPhase.Finally)) Reject("OwnedCleanupBeforeHookFinally");
                                var call = Call(id);
                                if (call == null || Read(state, call.Receiver) != Resource(index) + "/instance") Reject("CleanupReceiverIsNotTransferredSession");
                                if (declared[index].Cleanup != null) CheckCall(id, declared[index].Cleanup!, true);
                                state = state with { Released = state.Released.Add(index) };
                                break;
                        }
                    }
                    var pure = id == null || id.Phase is SendExecutionPhase.Entered or SendExecutionPhase.CommitSession or SendExecutionPhase.DetachSession or
                        SendExecutionPhase.SessionPresent or SendExecutionPhase.SessionResult;
                    foreach (var completion in id?.Phase == SendExecutionPhase.ValidateSessionResult
                        ? new[] { (Outcome: Read(state, Resource(id.Session) + "/factory-result") == null ? SendControlOutcome.Failure : SendControlOutcome.Success, Canceled: false) }
                        : pure ? new[] { (Outcome: SendControlOutcome.Always, Canceled: false) } :
                        new[] { (Outcome: SendControlOutcome.Success, Canceled: false), (Outcome: SendControlOutcome.Failure, Canceled: false), (Outcome: SendControlOutcome.Failure, Canceled: true) })
                    {
                        var outcome = completion.Outcome;
                        var next = state;
                        if (id != null && IsBodyCallback(id.Phase))
                            next = next with
                            {
                                CompletedCallbacks = next.CompletedCallbacks.Add(id.Phase),
                                SuccessfulCallbacks = outcome == SendControlOutcome.Success ? next.SuccessfulCallbacks.Add(id.Phase) : next.SuccessfulCallbacks
                            };
                        if (id != null && outcome == SendControlOutcome.Success)
                        {
                            var resource = Resource(id.Session);
                            if (id.Phase == SendExecutionPhase.Resolve && callbackSession)
                            {
                                var resolution = schedule.ServiceResolutions.Where(r => r.At == id).ToArray();
                                if (resolution.Length != 1 || resolution[0].ServiceType != declared[0].FactoryServiceType || resolution[0].ResultSlot != Service(branch, stage) + "/resolved")
                                    Reject("SessionFactoryDIResolutionTypeOrSlotMismatch");
                                else next = next with { Values = next.Values.SetItem(resolution[0].ResultSlot, "borrowed-factory:" + Resource(0)) };
                            }
                            if (id.Phase == SendExecutionPhase.SessionGuard) next = next with { Guarded = next.Guarded.Add(id.Session) };
                            if (id.Phase == SendExecutionPhase.ResolveSessionFactory) next = next with { Values = next.Values.SetItem(resource + "/factory/resolved", "borrowed-factory:" + resource) };
                            if (id.Phase == SendExecutionPhase.AcquireSession)
                            {
                                var call = Call(id);
                                if (call == null || call.Result != resource + "/factory-result" || Read(next, call.Receiver) != "borrowed-factory:" + resource) Reject("FactoryCallReceiverOrResultMismatch");
                                else next = next with { Values = next.Values.SetItem(call.Result, resource + "/instance") };
                                if (!callbackSession) next = next with { Acquired = next.Acquired.Add(id.Session) };
                            }
                            if (id.Phase == SendExecutionPhase.ValidateSessionResult) next = next with { ValidatedSessions = next.ValidatedSessions.Add(id.Session) };
                        }
                        if (outcome == SendControlOutcome.Failure)
                        {
                            var error = (completion.Canceled ? "cancel:" : "fault:") + id;
                            var call = id == null ? null : Call(id);
                            // Policies on actual calls specify natural replacement, never aggregation or reconstruction.
                            if (call != null && (!call.PreserveIdentityAndToken || !call.ReplacePreviousError || call.Aggregate)) Reject("SessionExceptionReplacementIdentityMismatch");
                            next = next with { Error = error, Failures = next.Failures.Add(error), FirstFailure = next.FirstFailure ?? id };
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
                        else
                        {
                            work.Push(next with { Point = selected[0].To });
                            // A violating factory may return null despite its public nonnullable contract.
                            if (callbackSession && id?.Phase == SendExecutionPhase.AcquireSession && outcome == SendControlOutcome.Success && Call(id)?.Result is string resultSlot)
                                work.Push(next with { Point = selected[0].To, Values = next.Values.SetItem(resultSlot, null) });
                        }
                    }
                }
                bool ReadyForCleanup(State state)
                {
                    // A startup failure may release partial acquisitions without entering this body.
                    if (!state.Entered) return state.Error != null && state.FirstFailure?.Phase is
                        SendExecutionPhase.ArgumentGuard or SendExecutionPhase.CancellationGuard or SendExecutionPhase.Resolve or
                        SendExecutionPhase.SessionGuard or SendExecutionPhase.ResolveSessionFactory or SendExecutionPhase.AcquireSession or SendExecutionPhase.ValidateSessionResult;
                    if (stage == route.Stages.Count) return state.CompletedCallbacks.Contains(SendExecutionPhase.InvokeHandler);
                    if (route.Stages[stage].Kind == FusionStageKind.Wrapper) return state.CompletedCallbacks.Contains(SendExecutionPhase.InvokeWrapper);
                    var callbacks = route.Stages[stage].Callbacks;
                    bool Has(BoundCallbackPhase phase) => callbacks.Any(c => c.Phase == phase);
                    var failedBefore = state.FirstFailure?.Phase == SendExecutionPhase.Before &&
                        state.CompletedCallbacks.Contains(SendExecutionPhase.Before) && !state.SuccessfulCallbacks.Contains(SendExecutionPhase.Before);
                    if (!failedBefore && !state.CompletedCallbacks.Contains(SendExecutionPhase.InvokeInner)) return false;
                    if (state.SuccessfulCallbacks.Contains(SendExecutionPhase.InvokeInner) && Has(BoundCallbackPhase.Success) &&
                        !state.CompletedCallbacks.Contains(SendExecutionPhase.Success)) return false;
                    var observedFailure = state.FirstFailure?.Phase is SendExecutionPhase.Before or SendExecutionPhase.InvokeInner or SendExecutionPhase.Success;
                    if (observedFailure && Has(BoundCallbackPhase.Exception) && !state.CompletedCallbacks.Contains(SendExecutionPhase.Exception)) return false;
                    return !Has(BoundCallbackPhase.Finally) || state.CompletedCallbacks.Contains(SendExecutionPhase.Finally);
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
                        else if (callbackSession && !awaits[0].RetainedSlots.Contains(Service(branch, stage) + "/resolved")) Reject("SessionFactoryLostAcrossPendingAwait");
                    }
                    else if (awaits.Length != 0) Reject("SynchronousSessionCallbackAwaited");
                }
            }
        return new(new(facts.OrderBy(f => f.Reason, StringComparer.Ordinal)), new(evidence));
    }

    private static string? Read(State state, string slot) => state.Values.TryGetValue(slot, out var value) ? value : null;
    private static bool IsBodyCallback(SendExecutionPhase phase) => phase is SendExecutionPhase.Before or SendExecutionPhase.InvokeInner or
        SendExecutionPhase.InvokeHandler or SendExecutionPhase.InvokeWrapper or SendExecutionPhase.Success or SendExecutionPhase.Exception or SendExecutionPhase.Finally;
}
