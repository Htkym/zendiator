using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamGraphValidator
{
    internal static EquatableArray<CanonicalExecutionGraphFact> CheckContract(BoundPipelineModel route)
    {
        var facts = new List<CanonicalExecutionGraphFact>();
        void Reject(string reason, string? anchor = null) => facts.Add(new(reason, anchor ?? route.Anchor));
        if (route.ContractOrigin != FusionContractOrigin.NewCanonical) Reject("NewCanonicalOriginRequired");
        if (route.Family != BoundDispatchFamily.Stream || route.ResultKind != FusionResultKind.Item || route.Result == null) Reject("StreamItemShapeRequired");
        if (route.Handlers.Count != 1) Reject("SingleStreamHandlerRequired");
        if (route.StreamItemFlow is { } items)
        {
            if (items.Stages.Count != route.Stages.Count || items.Source.Identity != route.Handlers.FirstOrDefault()?.Result || items.Final.Identity != route.Result)
                Reject("StreamItemFlowEndpointMismatch");
            var previous = items.Source;
            for (var i = 0; i < route.Stages.Count; i++)
            {
                var flow = route.Stages[i].StreamItems;
                if (flow == null || flow.Input != previous || i >= items.Stages.Count || flow != items.Stages[i])
                    Reject("StreamItemConnectionMismatch", route.Stages[i].Anchor);
                if (flow != null)
                {
                    if (route.Stages[i].Kind == FusionStageKind.Wrapper && flow.Input != flow.Output) Reject("WrapperItemBoundaryMismatch", route.Stages[i].Anchor);
                    previous = flow.Output;
                }
            }
            if (previous != items.Final) Reject("StreamFinalItemMismatch");
        }
        foreach (var handler in route.Handlers)
        {
            CheckSessions(handler.Type, handler.ServiceOwnership, handler.Sessions, handler.Anchor, "Handler");
            if (handler.CompilerOwnsError || handler.Callback == null) { Reject("HandlerImplementationUnavailable", handler.Anchor); continue; }
            if (handler.Callback.Phase != BoundCallbackPhase.Handler || handler.Callback.Completion != BoundCompletionKind.ValueTask || !handler.Callback.RequiresSingleConsumption)
                Reject("StreamHandlerCompletionMismatch", handler.Anchor);
            CheckParameters(handler.Callback, handler.Anchor);
        }
        foreach (var stage in route.Stages)
        {
            CheckSessions(stage.Type, stage.ServiceOwnership, stage.Sessions, stage.Anchor, "Stage");
            if (stage.CompilerOwnsError || stage.Callbacks.Count != 1) Reject("SingleRoleStreamStageRequired", stage.Anchor);
            if (stage.Kind is not (FusionStageKind.Hook or FusionStageKind.Wrapper)) Reject("SingleRoleStreamStageRequired", stage.Anchor);
            foreach (var callback in stage.Callbacks)
            {
                if (stage.Kind == FusionStageKind.Hook && (callback.Phase is not (BoundCallbackPhase.Filter or BoundCallbackPhase.Map) ||
                    callback.Completion is not (BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask) ||
                    callback.RequiresSingleConsumption != (callback.Completion == BoundCompletionKind.ValueTask))) Reject("SameItemHookCompletionMismatch", stage.Anchor);
                if (stage.Kind == FusionStageKind.Wrapper && (callback.Phase != BoundCallbackPhase.Wrapper ||
                    callback.Completion != BoundCompletionKind.ValueTask || !callback.RequiresSingleConsumption)) Reject("StreamWrapperCompletionMismatch", stage.Anchor);
                CheckParameters(callback, stage.Anchor);
            }
        }
        return new(facts.Distinct());
        void CheckSessions(string type, FusionOwnership ownership, EquatableArray<BoundSessionModel> sessions, string anchor, string role)
        {
            if (sessions.Count == 0)
            {
                if (ownership != FusionOwnership.BorrowedService) Reject("Owned" + role + "AcquisitionAndCleanupUnavailable", anchor);
                return;
            }
            if (sessions.Count != 1 || !sessions[0].SuppliesCallbackReceiver) { Reject("StreamSessionAcquisitionAndCleanupUnavailable", anchor); return; }
            var session = sessions[0];
            if (session.ResourceType != type || session.Ownership != ownership) Reject("SessionReceiverTypeOrOwnershipMismatch", anchor);
            if (string.IsNullOrEmpty(session.FactoryServiceType) || session.Acquire.Phase != BoundCallbackPhase.SessionAcquire ||
                session.Acquire.Completion is not (BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask) ||
                session.Acquire.RequiresSingleConsumption != (session.Acquire.Completion == BoundCompletionKind.ValueTask)) Reject("SessionAcquisitionContractMismatch", anchor);
            CheckParameters(session.Acquire, anchor);
            if (ownership == FusionOwnership.BorrowedResource)
            {
                if (session.Cleanup != null) Reject("BorrowedResourceHasCleanup", anchor);
            }
            else if (ownership != FusionOwnership.OwnedSession) Reject("ExplicitResourceOwnershipRequired", anchor);
            else if (session.Cleanup == null || session.Cleanup.Phase != BoundCallbackPhase.SessionCleanup ||
                session.Cleanup.Completion != BoundCompletionKind.ValueTask || !session.Cleanup.RequiresSingleConsumption) Reject("OwnedStreamSessionAsyncCleanupRequired", anchor);
            else CheckParameters(session.Cleanup, anchor);
        }
        void CheckParameters(BoundCallbackModel callback, string anchor)
        {
            if (callback.Parameters.Any(p => p.RefKind != "None" || p.ScopedKind != "None")) Reject("RetainedParameterRefSafetyUnavailable", anchor);
        }
    }

}
