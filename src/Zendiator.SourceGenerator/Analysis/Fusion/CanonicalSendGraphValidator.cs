using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalSendGraphValidator
{
    internal static EquatableArray<CanonicalExecutionGraphFact> CheckContract(BoundPipelineModel route)
    {
        var facts = new List<CanonicalExecutionGraphFact>();
        void Reject(string reason, string? anchor = null) => facts.Add(new(reason, anchor ?? route.Anchor));
        if (route.ContractOrigin != FusionContractOrigin.NewCanonical) Reject("NewCanonicalOriginRequired");
        if (route.Family is not (BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync))
            Reject("SourceMoveCurrentDisposeAndEnumerationOwnerGraphUnavailable");
        if (route.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void) ||
            (route.ResultKind == FusionResultKind.Response) != (route.Result != null)) Reject("SendResultShapeRequired");
        var async = route.Family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync;
        var all = route.Family is BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync;
        if (route.Handlers.Count == 0 || (!all && route.Handlers.Count != 1)) Reject("HandlerCountRequired");
        if (all && !route.ExplicitHandlerOrder) Reject("AuthoritativeBranchOrderRequired");
        foreach (var handler in route.Handlers)
        {
            CheckSessions(handler.Sessions, handler.Anchor);
            if (handler.ServiceOwnership != FusionOwnership.BorrowedService) Reject("OwnedHandlerAcquisitionAndCleanupUnavailable", handler.Anchor);
            if (handler.CompilerOwnsError || handler.Callback == null) { Reject("HandlerImplementationUnavailable", handler.Anchor); continue; }
            if (handler.Callback.Phase != BoundCallbackPhase.Handler || handler.Callback.Completion !=
                (async ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous)) Reject("HandlerCompletionMismatch", handler.Anchor);
            CheckCallback(handler.Callback, handler.Anchor);
        }
        foreach (var stage in route.Stages)
        {
            CheckSessions(stage.Sessions, stage.Anchor);
            if (stage.ServiceOwnership != FusionOwnership.BorrowedService) Reject("OwnedStageAcquisitionAndCleanupUnavailable", stage.Anchor);
            if (stage.CompilerOwnsError || stage.Callbacks.Count == 0) Reject("StageImplementationUnavailable", stage.Anchor);
            if (stage.Callbacks.GroupBy(c => c.Phase).Any(g => g.Count() != 1)) Reject("DuplicateCallbackPhase", stage.Anchor);
            if (stage.Kind == FusionStageKind.Wrapper)
            {
                if (stage.Callbacks.Count != 1 || stage.Callbacks[0].Phase != BoundCallbackPhase.Wrapper ||
                    stage.Callbacks[0].Completion != (async ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous))
                    Reject("OpaqueContinuationModeMismatch", stage.Anchor);
            }
            else if (stage.Kind != FusionStageKind.Hook || stage.Callbacks.Any(c => c.Phase is not
                (BoundCallbackPhase.Before or BoundCallbackPhase.Success or BoundCallbackPhase.Exception or BoundCallbackPhase.Finally)))
                Reject("SendHookPhaseRequired", stage.Anchor);
            foreach (var callback in stage.Callbacks) CheckCallback(callback, stage.Anchor);
        }
        return new(facts);

        void CheckSessions(EquatableArray<BoundSessionModel> sessions, string anchor)
        {
            foreach (var session in sessions)
            {
                if (string.IsNullOrEmpty(session.FactoryServiceType) || string.IsNullOrEmpty(session.ResourceType)) Reject("ExplicitSessionFactoryRequired", anchor);
                if (session.Acquire.Phase != BoundCallbackPhase.SessionAcquire) Reject("SessionAcquisitionPhaseRequired", anchor);
                CheckCallback(session.Acquire, anchor);
                if (session.Ownership is not (FusionOwnership.OwnedSession or FusionOwnership.BorrowedResource)) Reject("ExplicitResourceOwnershipRequired", anchor);
                if (session.Ownership == FusionOwnership.BorrowedResource)
                {
                    if (session.Cleanup != null) Reject("BorrowedResourceHasCleanup", anchor);
                }
                else if (session.Cleanup == null) Reject("OwnedSessionCleanupRequired", anchor);
                else
                {
                    CheckCallback(session.Cleanup, anchor);
                    if (session.Cleanup.Phase != BoundCallbackPhase.SessionCleanup || session.Cleanup.Completion !=
                        (async ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous)) Reject("SessionCleanupModeMismatch", anchor);
                }
            }
        }

        void CheckCallback(BoundCallbackModel callback, string anchor)
        {
            if (callback.Completion is not (BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask) ||
                (!async && callback.Completion != BoundCompletionKind.Synchronous))
                Reject("CallbackCompletionUnavailable", anchor);
            if (callback.RequiresSingleConsumption != (callback.Completion == BoundCompletionKind.ValueTask)) Reject("SingleConsumptionContractMismatch", anchor);
            if (callback.Parameters.Any(p => p.RefKind != "None" || p.ScopedKind != "None")) Reject("RetainedParameterRefSafetyUnavailable", anchor);
        }
    }

    internal static EquatableArray<CanonicalExecutionGraphFact> Validate(BoundPipelineModel route, CanonicalSendExecutionSchedule candidate)
    {
        var initial = CheckContract(route);
        if (initial.Count != 0) return initial;
        var facts = new List<CanonicalExecutionGraphFact>();
        void Reject(string reason, string? anchor = null) => facts.Add(new(reason, anchor ?? route.Anchor));
        // This is independent of candidate.Sites, candidate.Awaits and candidate.Owners.
        var required = RequiredSendExecutionContract.Derive(route);
        var sites = candidate.Sites.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var group in sites.Values.Where(g => g.Length > 1)) Reject("DuplicateSite", group[0].Anchor);
        foreach (var expected in required.Sites)
        {
            if (!sites.TryGetValue(expected.Id, out var found)) Reject("MissingSite:" + expected.Id.Phase, expected.Anchor);
            else if (found.Any(s => s != expected)) Reject("SiteContractMismatch:" + expected.Id.Phase, expected.Anchor);
        }
        foreach (var extra in candidate.Sites.Where(s => !required.Sites.Any(r => r.Id == s.Id))) Reject("UnexpectedSite:" + extra.Id.Phase, extra.Anchor);
        Compare(required.Orders, candidate.Orders, "Order");
        Compare(required.Owners, candidate.Owners, "Owner");
        Compare(required.States, candidate.States, "State");
        Compare(required.Scopes, candidate.Scopes, "Scope");
        Compare(required.Awaits, candidate.Awaits, "Await");
        Compare(required.Transfers, candidate.Transfers, "ValueTransfer");
        Compare(required.SessionTransfers, candidate.SessionTransfers, "SessionTransfer");
        Compare(required.SessionCalls, candidate.SessionCalls, "SessionCall");
        facts.AddRange(CanonicalSendControlValidator.Validate(route, candidate));
        facts.AddRange(CanonicalSendSessionValidator.Validate(route, candidate));
        if (candidate.ContextModelId != "operation-callback-v1") Reject("ContextBoundaryMismatch");
        if (candidate.EscapingOceIsCanceled != required.Async) Reject("EscapingOceBoundaryMismatch");

        var owners = candidate.Owners.GroupBy(o => o.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (owners.Count != candidate.Owners.Count) Reject("DuplicateOwner");
        foreach (var owner in candidate.Owners)
        {
            if (owner.Borrowed && owner.Cleanup != FusionCleanupKind.None) Reject("BorrowedOwnerHasCleanup");
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var cursor = owner;
            while (true)
            {
                if (!visited.Add(cursor.Id)) { Reject("OwnerCycle"); break; }
                if (cursor.Parent == null) break;
                if (!owners.TryGetValue(cursor.Parent, out var parent)) { Reject("MissingOwnerParent"); break; }
                cursor = parent;
            }
        }
        foreach (var site in candidate.Sites)
            if (!owners.ContainsKey(site.Owner)) Reject("MissingSiteOwner", site.Anchor);
        foreach (var order in candidate.Orders)
            if (!sites.ContainsKey(order.Before) || !sites.ContainsKey(order.After)) Reject("MissingOrderEndpoint");
        foreach (var group in candidate.Awaits.GroupBy(a => a.Producer))
        {
            if (group.Count() != 1) Reject("DuplicateAwaitConsumer");
            foreach (var awaitSite in group)
            {
                if (!sites.TryGetValue(awaitSite.Producer, out var producers) || producers.Any(p => p.Completion != BoundCompletionKind.ValueTask))
                    Reject("AwaitWithoutValueTaskProducer");
                if (!awaitSite.ConsumeOnce || !awaitSite.ConfigureAwaitFalse) Reject("AwaitConsumptionMismatch");
            }
        }
        return new(facts.Distinct());

        void Compare<T>(IEnumerable<T> expected, IEnumerable<T> actual, string kind)
        {
            // Multiset equality catches duplicates as well as missing and extra obligations.
            var remaining = expected.ToList();
            foreach (var value in actual)
            {
                var index = remaining.FindIndex(v => EqualityComparer<T>.Default.Equals(v, value));
                if (index < 0) Reject("Unexpected" + kind);
                else remaining.RemoveAt(index);
            }
            if (remaining.Count != 0) Reject("Missing" + kind);
        }
    }
}
