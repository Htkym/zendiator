using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamFallbackProjection
{
    internal static CanonicalStreamFallbackModel? Create(CanonicalRouteGraph row)
    {
        var c = row.Input.Composition; var route = row.Input.Route; var graph = row.Graph;
        if (graph?.IsValid != true || c.GeneratedNamespace == null || c.Errors.Count != 0 || c.CompilerOwnsHandlerError ||
            c.ExternAliases.Count != 0 || c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" ||
            route.Family != BoundDispatchFamily.Stream || route.Handlers.Count != 1) return null;
        var handler = route.Handlers[0]; var call = handler.StreamEmission;
        if (call == null || call.HandlerIsValueType || handler.CompilerOwnsError || !Receiver(handler.ServiceOwnership, handler.Sessions)) return null;
        var hooks = new List<BoundStreamHookEmission>();
        foreach (var stage in route.Stages)
        {
            if (stage.CompilerOwnsError || !Receiver(stage.ServiceOwnership, stage.Sessions)) return null;
            if (stage.Kind == FusionStageKind.Hook && stage.StreamEmission is { HookIsValueType: false } hook &&
                (hook.Phase is BoundCallbackPhase.Filter or BoundCallbackPhase.Map) &&
                (hook.Completion is BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask))
            { hooks.Add(hook with { Factory = Factory(stage.Sessions) }); continue; }
            if (stage.Kind == FusionStageKind.Wrapper && stage.StreamWrapperEmission is
                { WrapperIsValueType: false, Completion: BoundCompletionKind.Enumerable }) continue;
            return null;
        }
        var segments = new List<CanonicalStreamFallbackSegment>(); int consumer;
        if (graph.StreamSchedule is { } plain)
        {
            if (route.Stages.Any(s => s.Kind != FusionStageKind.Hook) || CanonicalStreamGraphValidator.Validate(route, plain).Count != 0) return null;
            segments.Add(new(0, null, null, new(hooks), plain)
            { SourceItemName = route.StreamItemFlow?.Source.Name, OutputItemName = route.StreamItemFlow?.Final.Name }); consumer = 0;
        }
        else if (graph.StreamWrapperSchedule is { } wrapped)
        {
            if (CanonicalStreamWrapperValidator.Validate(route, wrapped).Count != 0) return null;
            var indices = wrapped.Segments.Select((s, i) => (s.Descriptor.Id, Index: i)).ToDictionary(s => s.Id, s => s.Index);
            foreach (var segment in wrapped.Segments)
            {
                var d = segment.Descriptor;
                segments.Add(new(indices[d.Id], d.UpstreamSegment == null ? null : indices[d.UpstreamSegment],
                    d.SourcePosition < 0 ? null : route.Stages[d.SourcePosition].StreamWrapperEmission,
                    new(d.HookPositions.Select(p => route.Stages[p].StreamEmission! with { Factory = Factory(route.Stages[p].Sessions) })), segment.Cursor)
                {
                    WrapperFactory = d.SourcePosition < 0 ? null : Factory(route.Stages[d.SourcePosition].Sessions),
                    SourceItemName = route.StreamItemFlow == null ? null : d.SourcePosition < 0 ? route.StreamItemFlow.Source.Name : route.Stages[d.SourcePosition].StreamItems!.Output.Name,
                    OutputItemName = route.StreamItemFlow == null ? null : d.HookPositions.Count == 0
                        ? d.SourcePosition < 0 ? route.StreamItemFlow.Source.Name : route.Stages[d.SourcePosition].StreamItems!.Output.Name
                        : route.Stages[d.HookPositions[d.HookPositions.Count - 1]].StreamItems!.Output.Name
                });
            }
            consumer = indices[wrapped.ConsumerSegment];
        }
        else return null;
        // The canonical template implements this exact contract; no arbitrary schedule rewrite.
        if (segments.Any(s => s.Schedule.ContextModelId != "stream-call-caller-v1" || !s.Schedule.FreshCursorPerEnumeration ||
            s.Schedule.RequestSnapshot || s.Schedule.ReLendPublicCursor || s.Schedule.Awaits.Any(a => !a.ConsumeOnce || !a.ConfigureAwaitFalse) ||
            s.Schedule.Owners.Any(o => o.Borrowed && o.Cleanup != StreamCleanup.None))) return null;
        var id = CanonicalSplitSourceEmitter.Id(c.Identity + "|Stream|" + route.Request);
        return new(c.GeneratedNamespace, "__ZendiatorStreamCanonical_" + id, "Zendiator.StreamCanonical." + id + ".g.cs",
            call, new(segments), consumer)
        { Generic = route.StreamGeneric, ItemFlow = route.StreamItemFlow, HandlerFactory = Factory(handler.Sessions) };
    }
    private static BoundAsyncFactoryEmission? Factory(EquatableArray<BoundSessionModel> sessions) =>
        sessions.Count == 1 ? CanonicalCapabilityEmissionNormalizer.Factory(sessions[0], false) : null;
    private static bool Receiver(FusionOwnership ownership, EquatableArray<BoundSessionModel> sessions) =>
        sessions.Count == 0 ? ownership == FusionOwnership.BorrowedService : sessions.Count == 1 &&
        ownership == sessions[0].Ownership && Factory(sessions) is { FactoryIsValueType: false };
}
