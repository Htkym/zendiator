using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

// Retain declared source-to-consumer order; runtime cursors own execution and cleanup.
internal static class CanonicalStreamFallbackProjection
{
    internal static CanonicalStreamFallbackModel? Create(CanonicalRouteGraph row)
    {
        var c = row.Input.Composition; var route = row.Input.Route;
        if (row.Graph is not { IsValid: true, NativeStream: true } || c.GeneratedNamespace == null ||
            c.Errors.Count != 0 || c.CompilerOwnsHandlerError || c.ExternAliases.Count != 0 ||
            c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" ||
            route.Family != BoundDispatchFamily.Stream || route.Handlers.Count != 1) return null;
        var handler = route.Handlers[0]; var call = handler.StreamEmission;
        if (call == null || string.IsNullOrEmpty(call.CursorName) || handler.CompilerOwnsError ||
            !Receiver(handler.ServiceOwnership, handler.Sessions)) return null;
        var segments = new List<CanonicalStreamFallbackSegment>();
        var hooks = new List<BoundStreamHookEmission>();
        BoundWrapperEmission? wrapper = null;
        BoundAsyncFactoryEmission? wrapperFactory = null;
        int? upstream = null;
        var sourceItem = route.StreamItemFlow?.Source.Name ?? call.ItemName;
        foreach (var stage in route.Stages)
        {
            if (stage.CompilerOwnsError || !Receiver(stage.ServiceOwnership, stage.Sessions)) return null;
            if (stage.Kind == FusionStageKind.Hook && stage.StreamEmission is { } hook &&
                hook.Phase is BoundCallbackPhase.Filter or BoundCallbackPhase.Map &&
                hook.Completion is BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask)
            {
                hooks.Add(hook with { Factory = Factory(stage.Sessions) });
                continue;
            }
            if (stage.Kind != FusionStageKind.Wrapper || stage.StreamWrapperEmission is not
                { Completion: BoundCompletionKind.ValueTask } middleware) return null;
            Flush();
            upstream = segments.Count - 1;
            sourceItem = stage.StreamItems?.Output.Name ?? sourceItem;
            wrapper = middleware;
            wrapperFactory = Factory(stage.Sessions);
            hooks.Clear();
        }
        Flush();
        var id = CanonicalSplitSourceEmitter.Id(c.Identity + "|Stream|" + route.Request);
        return new(c.GeneratedNamespace, "__ZendiatorStreamCanonical_" + id, "Zendiator.StreamCanonical." + id + ".g.cs",
            call, new(segments), segments.Count - 1)
        { Generic = route.StreamGeneric, ItemFlow = route.StreamItemFlow, HandlerFactory = Factory(handler.Sessions) };

        void Flush()
        {
            var outputItem = hooks.Count == 0 ? sourceItem : hooks[hooks.Count - 1].OutputItemName ?? sourceItem;
            segments.Add(new(segments.Count, upstream, wrapper, new(hooks))
            { WrapperFactory = wrapperFactory, SourceItemName = sourceItem, OutputItemName = outputItem });
            sourceItem = outputItem;
        }
    }

    private static BoundAsyncFactoryEmission? Factory(EquatableArray<BoundSessionModel> sessions) =>
        sessions.Count == 1 ? CanonicalCapabilityEmissionNormalizer.Factory(sessions[0], false) : null;
    private static bool Receiver(FusionOwnership ownership, EquatableArray<BoundSessionModel> sessions) =>
        sessions.Count == 0 ? ownership == FusionOwnership.BorrowedService : sessions.Count == 1 &&
        ownership == sessions[0].Ownership && Factory(sessions) != null;
}
