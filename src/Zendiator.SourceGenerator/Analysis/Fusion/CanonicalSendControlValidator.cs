using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static Zendiator.SourceGenerator.SendExecutionNames;

namespace Zendiator.SourceGenerator;

/// <summary>
/// Checks reachable conditional paths, separately from Orders multiset equality.
/// Callee outcomes summarize independently checked child invocation templates; opaque user bodies are not analyzed.
/// </summary>
internal static class CanonicalSendControlValidator
{
    private sealed record Contract(string Owner, int Branch, int Stage, bool Operation, bool Next,
        bool Before, bool Success, bool Observer, bool Finally, bool Span, EquatableArray<SendExecutionSiteId> Sites);
    private sealed record Path(SendControlPoint Point, bool Failed, int Guards, bool Entered, int Before,
        int Invocation, int Success, int Observer, int Finally, int NextBranch, SendExecutionPhase? FirstFailure,
        ImmutableHashSet<SendControlPoint> Visited)
    {
        public bool BeforeSucceeded { get; init; }
        public bool InvocationCompleted { get; init; }
        public bool InvocationSucceeded { get; init; }
        public bool SuccessCompleted { get; init; }
        public bool ObserverCompleted { get; init; }
        public bool FinallyCompleted { get; init; }
        public bool SessionCleanupStarted { get; init; }
        public bool Returned { get; init; }
        public ImmutableHashSet<int> CommittedSessions { get; init; } = ImmutableHashSet<int>.Empty;
        public ImmutableHashSet<int> DetachedSessions { get; init; } = ImmutableHashSet<int>.Empty;
    }

    internal static EquatableArray<CanonicalExecutionGraphFact> Validate(BoundPipelineModel route, CanonicalSendExecutionSchedule schedule)
    {
        var facts = new HashSet<CanonicalExecutionGraphFact>();
        void Reject(string reason) => facts.Add(new(reason, route.Anchor));
        var contracts = DeriveContracts(route).ToArray(); // Only the bound contract supplies required points and path obligations.
        var flows = schedule.ControlFlows.GroupBy(f => f.Owner, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        foreach (var extra in flows.Keys.Where(k => !contracts.Any(c => c.Owner == k))) Reject("UnexpectedControlFlow");
        foreach (var contract in contracts)
        {
            if (!flows.TryGetValue(contract.Owner, out var matches)) { Reject("MissingControlFlow"); continue; }
            if (matches.Length != 1) Reject("DuplicateControlFlow");
            CheckFlow(contract, matches[0]);
        }
        foreach (var transfer in schedule.Transfers)
        {
            if (transfer.Condition is not (SendTransferCondition.InvocationStart or SendTransferCondition.Entered or SendTransferCondition.ProducerSuccess or
                SendTransferCondition.EachNextStart or SendTransferCondition.BranchStart)) Reject("UnknownValueTransferCondition");
            if (transfer.Source.Kind == SendValueKind.BeforeResult && (transfer.At.Phase != SendExecutionPhase.Before ||
                transfer.Condition != SendTransferCondition.ProducerSuccess)) Reject("BeforeResultTransferredWithoutSuccess");
        }
        return new(facts.OrderBy(f => f.Reason, StringComparer.Ordinal));

        void CheckFlow(Contract contract, SendControlFlow flow)
        {
            var allowed = new HashSet<SendControlPoint>(contract.Sites.Select(s => Point(contract.Owner, s)))
                { Entry(contract.Owner), Exit(contract.Owner, true), Exit(contract.Owner, false) };
            var declared = new HashSet<SendControlPoint>(flow.Edges.SelectMany(e => new[] { e.From, e.To }));
            if (declared.Any(p => !allowed.Contains(p))) Reject("UnexpectedControlPoint");
            if (allowed.Any(p => !declared.Contains(p))) Reject("MissingControlPoint");
            foreach (var edge in flow.Edges)
            {
                if (edge.Outcome is not (SendControlOutcome.Always or SendControlOutcome.Success or SendControlOutcome.Failure)) Reject("UnknownControlOutcome");
                if (edge.Predicate is not (SendControlPredicate.Any or SendControlPredicate.HasFailure or SendControlPredicate.NoFailure or
                    SendControlPredicate.SpanDestination or SendControlPredicate.NoSpanDestination or SendControlPredicate.SessionPresent or SendControlPredicate.SessionAbsent)) Reject("UnknownControlPredicate");
            }
            var edges = flow.Edges.ToLookup(e => e.From);
            var reachable = new HashSet<SendControlPoint>();
            foreach (var span in contract.Span ? new[] { false, true } : new[] { false })
            {
                var work = new Stack<Path>();
                work.Push(new(Entry(contract.Owner), false, 0, false, 0, 0, 0, 0, 0, 0, null, ImmutableHashSet<SendControlPoint>.Empty));
                while (work.Count != 0)
                {
                    var state = work.Pop();
                    if (!allowed.Contains(state.Point)) continue;
                    if (state.Visited.Contains(state.Point))
                    {
                        Reject(state.Point.Site?.Phase == SendExecutionPhase.Finally ? "FinallyRepeated" : "ControlCycle");
                        continue;
                    }
                    reachable.Add(state.Point);
                    state = state with { Visited = state.Visited.Add(state.Point) };
                    if (state.Point.Terminal is SendControlTerminal.Success or SendControlTerminal.Failure)
                    {
                        if (!contract.Operation && !contract.Next) CheckDeclaredOrders(contract, state, Site(contract.Branch, contract.Stage, SendExecutionPhase.ScopeExit), span);
                        CheckExit(contract, state, span);
                        if (edges[state.Point].Any()) Reject("ControlLeavesTerminal");
                        continue;
                    }
                    var phase = state.Point.Site?.Phase;
                    if (phase != null)
                    {
                        CheckDeclaredOrders(contract, state, state.Point.Site!, span);
                        state = EnterPoint(contract, state, phase.Value, span);
                    }
                    var pure = phase == null || phase is SendExecutionPhase.Entered or SendExecutionPhase.OperationBoundary or
                        SendExecutionPhase.DestinationCommit or SendExecutionPhase.OperationReturn or SendExecutionPhase.CommitSession or
                        SendExecutionPhase.DetachSession or SendExecutionPhase.SessionPresent or SendExecutionPhase.SessionResult;
                    var outcomes = pure ? new[] { SendControlOutcome.Always } : new[] { SendControlOutcome.Success, SendControlOutcome.Failure };
                    if (edges[state.Point].Any(e => !outcomes.Contains(e.Outcome))) Reject("UnexpectedControlOutcome");
                    foreach (var outcome in outcomes)
                    {
                        var next = state with { Failed = state.Failed || outcome == SendControlOutcome.Failure,
                            FirstFailure = outcome == SendControlOutcome.Failure ? state.FirstFailure ?? phase : state.FirstFailure };
                        if (phase == SendExecutionPhase.Before) next = next with { BeforeSucceeded = outcome == SendControlOutcome.Success };
                        if (phase is SendExecutionPhase.InvokeInner or SendExecutionPhase.InvokeHandler or SendExecutionPhase.InvokeWrapper or SendExecutionPhase.NextCall)
                            next = next with { InvocationCompleted = true, InvocationSucceeded = outcome == SendControlOutcome.Success };
                        if (phase == SendExecutionPhase.Success) next = next with { SuccessCompleted = true };
                        if (phase == SendExecutionPhase.Exception) next = next with { ObserverCompleted = true };
                        if (phase == SendExecutionPhase.Finally) next = next with { FinallyCompleted = true };
                        if (outcome == SendControlOutcome.Success)
                        {
                            var bit = phase switch { SendExecutionPhase.ArgumentGuard => 1, SendExecutionPhase.CancellationGuard => 2,
                                SendExecutionPhase.Resolve => 4, SendExecutionPhase.DestinationGuard => 8, _ => 0 };
                            next = next with { Guards = next.Guards | bit };
                            if (contract.Operation && phase == SendExecutionPhase.BranchStart) next = next with { NextBranch = next.NextBranch + 1 };
                        }
                        if (phase == SendExecutionPhase.OperationBoundary) next = next with { Guards = next.Guards | 16 };
                        if (phase == SendExecutionPhase.DestinationCommit) next = next with { Guards = next.Guards | 32 };
                        var present = state.Point.Site != null && next.DetachedSessions.Contains(state.Point.Site.Session);
                        var choices = edges[state.Point].Where(e => e.Outcome == outcome && Predicate(e.Predicate, next.Failed, span, present)).ToArray();
                        if (choices.Length != 1)
                        {
                            Reject(choices.Length == 0 ? "MissingControlOutcome" : "AmbiguousControlOutcome");
                            continue;
                        }
                        work.Push(next with { Point = choices[0].To });
                    }
                }
            }
            if (allowed.Any(p => !reachable.Contains(p))) Reject("UnreachableRequiredControlPoint");
        }

        Path EnterPoint(Contract contract, Path state, SendExecutionPhase phase, bool span)
        {
            if (contract.Operation)
            {
                if (state.Returned) Reject("ControlContinuedAfterReturn");
                if (phase == SendExecutionPhase.OperationBoundary && ((state.Guards & 1) == 0 || state.Failed)) Reject("BoundaryBeforeArgumentSuccess");
                if (phase == SendExecutionPhase.DestinationGuard && !span) Reject("SpanGuardOnNonSpanPath");
                if (phase == SendExecutionPhase.BranchStart)
                {
                    if (state.Failed) Reject("BranchStartedAfterFailure");
                    if ((state.Guards & 17) != 17 || (span && (state.Guards & 8) == 0)) Reject("BranchNotDominatedByOperationGuards");
                    if (state.Point.Site!.Branch != state.NextBranch) Reject("BranchStartedWithoutPredecessorSuccess");
                }
                if (phase is SendExecutionPhase.DestinationCommit or SendExecutionPhase.OperationReturn)
                {
                    if (state.Failed || state.NextBranch != route.Handlers.Count) Reject("ResultPublishedBeforeAllBranchesSucceeded");
                    if (phase == SendExecutionPhase.DestinationCommit && !span) Reject("SpanCommitOnNonSpanPath");
                    if (phase == SendExecutionPhase.DestinationCommit && state.Returned) Reject("DestinationCommittedAfterReturn");
                    if (phase == SendExecutionPhase.OperationReturn && span && (state.Guards & 32) == 0) Reject("ReturnNotDominatedByDestinationCommit");
                }
                return phase == SendExecutionPhase.OperationReturn ? state with { Returned = true } : state;
            }
            if (contract.Next)
            {
                if (state.Failed) Reject("NextStartedAfterFailure");
                return state with { Invocation = state.Invocation + 1 };
            }
            if (phase == SendExecutionPhase.CancellationGuard && (state.Guards & 1) == 0) Reject("CancellationNotDominatedByArgument");
            if (phase == SendExecutionPhase.Resolve && (state.Guards & 3) != 3) Reject("ResolutionNotDominatedByGuards");
            if (phase == SendExecutionPhase.Entered)
            {
                if ((state.Guards & 7) != 7 || state.Failed) Reject("EnteredNotDominatedByGuardsAndResolution");
                return state with { Entered = true };
            }
            if (phase == SendExecutionPhase.CommitSession)
                return state with { CommittedSessions = state.CommittedSessions.Add(state.Point.Site!.Session) };
            if (phase is SendExecutionPhase.DetachSession or SendExecutionPhase.SessionPresent or SendExecutionPhase.CleanupSession)
            {
                if (!state.SessionCleanupStarted && !ReadyForSessionCleanup(contract, state)) Reject("SessionCleanupStartedBeforeBodyOutcome");
                state = state with { SessionCleanupStarted = true };
            }
            if (phase == SendExecutionPhase.DetachSession)
            {
                var session = state.Point.Site!.Session;
                return state with { DetachedSessions = state.CommittedSessions.Contains(session) ? state.DetachedSessions.Add(session) : state.DetachedSessions };
            }
            if (phase is SendExecutionPhase.SessionPresent or SendExecutionPhase.CleanupSession or SendExecutionPhase.SessionResult) return state;
            var callback = phase is SendExecutionPhase.Before or SendExecutionPhase.InvokeInner or SendExecutionPhase.InvokeHandler or
                SendExecutionPhase.InvokeWrapper or SendExecutionPhase.Success or SendExecutionPhase.Exception or SendExecutionPhase.Finally;
            if (callback && !state.Entered) Reject("CallbackReachedBeforeEntered");
            if (callback && state.SessionCleanupStarted) Reject("SessionBodyCallbackAfterDetach");
            if (state.Failed && phase is not (SendExecutionPhase.Exception or SendExecutionPhase.Finally)) Reject("ExecutionContinuedAfterFailure");
            switch (phase)
            {
                case SendExecutionPhase.Before:
                    if (state.Invocation != 0 || state.Success != 0 || state.Finally != 0) Reject("BeforeReachedAfterInnerOrUnwind");
                    return state with { Before = state.Before + 1 };
                case SendExecutionPhase.InvokeInner:
                    if (contract.Before && !state.BeforeSucceeded) Reject("InnerNotDominatedByBeforeSuccess");
                    goto case SendExecutionPhase.InvokeHandler;
                case SendExecutionPhase.InvokeHandler:
                case SendExecutionPhase.InvokeWrapper:
                    if (state.Failed) Reject("InvocationStartedAfterFailure");
                    if (state.Finally != 0) Reject("InvocationStartedAfterFinally");
                    return state with { Invocation = state.Invocation + 1 };
                case SendExecutionPhase.Success:
                    if (!state.InvocationSucceeded) Reject("SuccessNotDominatedByInnerSuccess");
                    if (state.Finally != 0) Reject("SuccessReachedAfterFinally");
                    return state with { Success = state.Success + 1 };
                case SendExecutionPhase.Exception:
                    if (!state.Failed) Reject("ObserverReachedOnSuccess");
                    if (state.Finally != 0) Reject("ObserverReenteredAfterFinally");
                    if (state.Observer != 0) Reject("ObserverRepeated");
                    return state with { Observer = state.Observer + 1 };
                case SendExecutionPhase.Finally:
                    if (state.Finally != 0) Reject("FinallyRepeated");
                    if (!state.InvocationCompleted && state.FirstFailure != SendExecutionPhase.Before) Reject("FinallyNotDominatedByInnerCompletion");
                    if (!state.Failed && contract.Success && !state.SuccessCompleted) Reject("FinallyNotDominatedBySuccessCompletion");
                    var observes = state.FirstFailure is SendExecutionPhase.Before or SendExecutionPhase.InvokeInner or SendExecutionPhase.Success;
                    if (state.Failed && contract.Observer && observes && !state.ObserverCompleted) Reject("FinallyNotDominatedByObserverCompletion");
                    return state with { Finally = state.Finally + 1 };
                default: return state;
            }
        }

        bool ReadyForSessionCleanup(Contract contract, Path state)
        {
            if (!state.Entered) return state.Failed && state.FirstFailure is
                SendExecutionPhase.ArgumentGuard or SendExecutionPhase.CancellationGuard or SendExecutionPhase.Resolve or
                SendExecutionPhase.SessionGuard or SendExecutionPhase.ResolveSessionFactory or SendExecutionPhase.AcquireSession;
            var hook = contract.Stage < route.Stages.Count && route.Stages[contract.Stage].Kind == FusionStageKind.Hook;
            if (!hook) return state.InvocationCompleted;
            var failedBefore = state.FirstFailure == SendExecutionPhase.Before && !state.BeforeSucceeded;
            if (!failedBefore && !state.InvocationCompleted) return false;
            if (state.InvocationSucceeded && contract.Success && !state.SuccessCompleted) return false;
            var observes = state.FirstFailure is SendExecutionPhase.Before or SendExecutionPhase.InvokeInner or SendExecutionPhase.Success;
            if (observes && contract.Observer && !state.ObserverCompleted) return false;
            return !contract.Finally || state.FinallyCompleted;
        }

        void CheckDeclaredOrders(Contract contract, Path state, SendExecutionSiteId after, bool span)
        {
            // Within one invocation, a required predecessor must be on the actual path.
            // Cross-invocation child exits are summarized by InvocationCompleted/InvocationSucceeded.
            foreach (var order in schedule.Orders.Where(o => o.After == after && contract.Sites.Contains(o.Before)))
            {
                var applies = order.Condition switch
                {
                    SendOrderCondition.Start => true,
                    SendOrderCondition.InnerSuccess => !state.Failed,
                    SendOrderCondition.Failure => state.Failed && state.FirstFailure == order.Before.Phase,
                    SendOrderCondition.Unwind => order.Before.Phase == SendExecutionPhase.Finally ? state.Entered && contract.Finally :
                        state.Failed && contract.Observer && state.FirstFailure is SendExecutionPhase.Before or SendExecutionPhase.InvokeInner or SendExecutionPhase.Success,
                    SendOrderCondition.SpanDestination => span,
                    SendOrderCondition.EachNext => true,
                    SendOrderCondition.AllBranchesSuccess => !state.Failed,
                    _ => false
                };
                if (applies && !state.Visited.Contains(Point(contract.Owner, order.Before))) Reject("ControlViolatesDeclaredOrder");
            }
        }

        void CheckExit(Contract contract, Path state, bool span)
        {
            var success = state.Point.Terminal == SendControlTerminal.Success;
            if (success == state.Failed) Reject("ControlExitStatusMismatch");
            if (contract.Operation)
            {
                if (success && (state.NextBranch != route.Handlers.Count || (span && (state.Guards & 32) == 0))) Reject("OperationExitedBeforeAllSuccessfulWork");
                return;
            }
            if (contract.Next) { if (state.Invocation != 1) Reject("NextInvocationMissing"); return; }
            if (success && (!state.Entered || (state.Guards & 7) != 7 || state.Invocation != 1 || state.Before != (contract.Before ? 1 : 0) ||
                state.Success != (contract.Success ? 1 : 0))) Reject("SuccessfulScopeSkippedRequiredWork");
            if (state.Entered && state.Finally != (contract.Finally ? 1 : 0)) Reject("EnteredScopeSkippedFinally");
            if (!state.Entered && (state.Observer != 0 || state.Finally != 0)) Reject("UnenteredScopeUnwound");
            if (!success && state.Entered)
            {
                var observes = state.FirstFailure is SendExecutionPhase.Before or SendExecutionPhase.InvokeInner or SendExecutionPhase.Success;
                if (state.Observer != (contract.Observer && observes ? 1 : 0)) Reject("FailureSkippedOrInventedObserver");
                if (state.FirstFailure == SendExecutionPhase.Before && state.Invocation != 0) Reject("InnerStartedAfterBeforeFailure");
            }
        }
    }

    private static bool Predicate(SendControlPredicate predicate, bool failed, bool span, bool present) => predicate switch
    {
        SendControlPredicate.Any => true, SendControlPredicate.HasFailure => failed, SendControlPredicate.NoFailure => !failed,
        SendControlPredicate.SpanDestination => span, SendControlPredicate.NoSpanDestination => !span,
        SendControlPredicate.SessionPresent => present, SendControlPredicate.SessionAbsent => !present, _ => false
    };

    private static IEnumerable<Contract> DeriveContracts(BoundPipelineModel route)
    {
        var operation = new List<SendExecutionSiteId> { Site(-1, -1, SendExecutionPhase.ArgumentGuard), Site(-1, -1, SendExecutionPhase.OperationBoundary), Site(-1, -1, SendExecutionPhase.OperationReturn) };
        var span = route.Family == BoundDispatchFamily.SendAllSync && route.ResultKind == FusionResultKind.Response;
        if (span) operation.AddRange(new[] { Site(-1, -1, SendExecutionPhase.DestinationGuard), Site(-1, -1, SendExecutionPhase.DestinationCommit) });
        for (var b = 0; b < route.Handlers.Count; b++) operation.Add(Site(b, -1, SendExecutionPhase.BranchStart));
        yield return new("operation", -1, -1, true, false, false, false, false, false, span, new(operation));
        for (var b = 0; b < route.Handlers.Count; b++)
            for (var s = 0; s <= route.Stages.Count; s++)
            {
                var handler = s == route.Stages.Count;
                var wrapper = !handler && route.Stages[s].Kind == FusionStageKind.Wrapper;
                var capabilities = handler || wrapper ? Array.Empty<BoundCallbackPhase>() : route.Stages[s].Callbacks.Select(c => c.Phase).ToArray();
                var phases = new List<SendExecutionPhase> { SendExecutionPhase.ArgumentGuard, SendExecutionPhase.CancellationGuard, SendExecutionPhase.Resolve,
                    SendExecutionPhase.Entered, handler ? SendExecutionPhase.InvokeHandler : wrapper ? SendExecutionPhase.InvokeWrapper : SendExecutionPhase.InvokeInner };
                if (capabilities.Contains(BoundCallbackPhase.Before)) phases.Add(SendExecutionPhase.Before);
                if (capabilities.Contains(BoundCallbackPhase.Success)) phases.Add(SendExecutionPhase.Success);
                if (capabilities.Contains(BoundCallbackPhase.Exception)) phases.Add(SendExecutionPhase.Exception);
                if (capabilities.Contains(BoundCallbackPhase.Finally)) phases.Add(SendExecutionPhase.Finally);
                var sessionSites = new List<SendExecutionSiteId>();
                var sessions = Sessions(route, b, s);
                for (var i = 0; i < sessions.Count; i++)
                {
                    var sessionPhases = new List<SendExecutionPhase> { SendExecutionPhase.SessionGuard, SendExecutionPhase.ResolveSessionFactory,
                        SendExecutionPhase.AcquireSession, SendExecutionPhase.CommitSession, SendExecutionPhase.DetachSession };
                    if (sessions[i].Ownership == FusionOwnership.OwnedSession) sessionPhases.AddRange(new[] { SendExecutionPhase.SessionPresent, SendExecutionPhase.CleanupSession });
                    sessionSites.AddRange(sessionPhases.Select(p => SessionSite(b, s, i, p)));
                }
                if (sessions.Count != 0) sessionSites.Add(SessionSite(b, s, -1, SendExecutionPhase.SessionResult));
                yield return new(Scope(b, s), b, s, false, false, capabilities.Contains(BoundCallbackPhase.Before), capabilities.Contains(BoundCallbackPhase.Success),
                    capabilities.Contains(BoundCallbackPhase.Exception), capabilities.Contains(BoundCallbackPhase.Finally), false, new(phases.Select(p => Site(b, s, p)).Concat(sessionSites)));
                if (wrapper) yield return new(Next(b, s), b, s, false, true, false, false, false, false, false, new(new[] { Site(b, s, SendExecutionPhase.NextCall) }));
            }
    }
}
