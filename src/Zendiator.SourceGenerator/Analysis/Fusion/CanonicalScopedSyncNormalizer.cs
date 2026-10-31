using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

// This proof owns scoped local execution; it is not a value-request Send schedule.
internal sealed record CanonicalScopedSyncProof(BoundSyncHandlerEmission Handler, BoundScopedObserverEmission Observer);

internal static class CanonicalScopedSyncNormalizer
{
    internal static bool IsObservation(BoundCallbackPhase phase) => phase is BoundCallbackPhase.ObserveBefore or
        BoundCallbackPhase.ObserveSuccess or BoundCallbackPhase.ObserveException or BoundCallbackPhase.ObserveFinally;

    internal static CanonicalExecutionGraphModel Graph(BoundPipelineModel route)
    {
        var facts = new List<CanonicalExecutionGraphFact>();
        void Reject(string reason) => facts.Add(new(reason, route.Anchor));
        if (route.ContractOrigin != FusionContractOrigin.NewCanonical || route.RequestShape != BoundRequestShape.ClosedRefLike ||
            route.RequestIsGeneric || route.Family != BoundDispatchFamily.SendSync || !route.ExplicitHandlerOrder ||
            route.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void))
            Reject("ScopedSynchronousClosedRequestRequired");
        var handler = route.Handlers.Count == 1 ? route.Handlers[0] : null;
        var stage = route.Stages.Count == 1 ? route.Stages[0] : null;
        if (handler is not
            {
                CompilerOwnsError: false, ServiceOwnership: FusionOwnership.BorrowedService,
                SyncEmission: { HandlerIsValueType: false, RequestIsRefLike: true }
            } || handler.Sessions.Count != 0 ||
            handler.SyncEmission.ResultName is not (null or "int" or "global::System.Int32") ||
            handler.Callback is not { Completion: BoundCompletionKind.Synchronous } callback || !Scoped(callback))
            Reject("ScopedDirectClassHandlerRequired");
        if (stage is not
            {
                Kind: FusionStageKind.Hook, CompilerOwnsError: false, ServiceOwnership: FusionOwnership.BorrowedService,
                ScopedObserver: { ObserverIsValueType: false }
            } || stage.Sessions.Count != 0 || stage.Callbacks.Count != 4 ||
            stage.Callbacks.Any(c => !IsObservation(c.Phase) || c.Completion != BoundCompletionKind.Synchronous || !Scoped(c)))
            Reject("ScopedDirectClassObservationRequired");
        return new(route, null, new(facts))
        {
            ScopedSync = facts.Count == 0 ? new(handler!.SyncEmission!, stage!.ScopedObserver!) : null
        };
    }

    private static bool Scoped(BoundCallbackModel callback) => callback.Parameters.Count >= 2 &&
        callback.Parameters[0] is { RefKind: "None", ScopedKind: "ScopedValue" } &&
        callback.Parameters.Skip(1).All(p => p.RefKind == "None" && p.ScopedKind == "None");
}
