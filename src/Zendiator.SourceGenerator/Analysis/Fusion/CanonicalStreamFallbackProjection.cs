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
        if (call == null || call.RequestIsValueType || call.HandlerIsValueType || handler.CompilerOwnsError ||
            handler.Sessions.Count != 0 || handler.ServiceOwnership != FusionOwnership.BorrowedService) return null;
        foreach (var stage in route.Stages)
        {
            if (stage.CompilerOwnsError || stage.Sessions.Count != 0 || stage.ServiceOwnership != FusionOwnership.BorrowedService) return null;
            if (stage.Kind == FusionStageKind.Hook && stage.StreamEmission is { HookIsValueType: false } hook &&
                hook.Phase is BoundCallbackPhase.Filter or BoundCallbackPhase.Map) continue;
            if (stage.Kind == FusionStageKind.Wrapper && stage.StreamWrapperEmission is
                { WrapperIsValueType: false, Completion: BoundCompletionKind.Enumerable }) continue;
            return null;
        }
        var segments = new List<CanonicalStreamFallbackSegment>(); int consumer;
        if (graph.StreamSchedule is { } plain)
        {
            if (route.Stages.Any(s => s.Kind != FusionStageKind.Hook) || CanonicalStreamGraphValidator.Validate(route, plain).Count != 0) return null;
            segments.Add(new(0, null, null, new(route.Stages.Select(s => s.StreamEmission!)), plain)); consumer = 0;
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
                    new(d.HookPositions.Select(p => route.Stages[p].StreamEmission!)), segment.Cursor));
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
            call, new(segments), consumer) { Generic = route.StreamGeneric };
    }
}
