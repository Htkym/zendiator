using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalStreamPrototypeProjection
{
    internal static CanonicalStreamPrototypeModel? Create(CanonicalRouteGraph row)
    {
        var route = row.Input.Route;
        var composition = row.Input.Composition;
        var schedule = row.Graph?.StreamSchedule;
        if (row.Graph?.IsValid != true || schedule == null || composition.GeneratedNamespace == null ||
            composition.Errors.Count != 0 || composition.CompilerOwnsHandlerError || composition.ExternAliases.Count != 0 ||
            composition.Facts.Count != 1 || composition.Facts[0].Key != "IncompleteExecutionContract" ||
            route.Family != BoundDispatchFamily.Stream || route.Handlers.Count != 1 || route.Stages.Count != 3) return null;
        var handler = route.Handlers[0];
        var call = handler.StreamEmission;
        if (call == null || handler.CompilerOwnsError || handler.ServiceOwnership != FusionOwnership.BorrowedService ||
            handler.Sessions.Count != 0 || call.HandlerIsValueType ||
            call.ItemName is not ("int" or "global::System.Int32")) return null;
        var expected = new[] { BoundCallbackPhase.Filter, BoundCallbackPhase.Map, BoundCallbackPhase.Filter };
        for (var i = 0; i < route.Stages.Count; i++)
        {
            var stage = route.Stages[i];
            if (stage.Kind != FusionStageKind.Hook || stage.CompilerOwnsError || stage.Sessions.Count != 0 ||
                stage.ServiceOwnership != FusionOwnership.BorrowedService || stage.StreamEmission is not { } hook ||
                hook.HookIsValueType || hook.Phase != expected[i]) return null;
            // Validated graph positions are the ordered list, not a type/phase sort.
            var phase = hook.Phase == BoundCallbackPhase.Filter ? StreamPhase.Filter : StreamPhase.Map;
            if (schedule.Sites.Count(s => s.Id == new StreamSiteId(StreamProgram.Move, phase, i)) != 1) return null;
        }
        if (schedule.ContextModelId != "stream-call-caller-v1" || !schedule.FreshCursorPerEnumeration ||
            schedule.RequestSnapshot || schedule.ReLendPublicCursor ||
            schedule.Awaits.Any(a => !a.ConsumeOnce || !a.ConfigureAwaitFalse) ||
            schedule.Owners.Any(o => o.Borrowed && o.Cleanup != StreamCleanup.None)) return null;
        var cleanup = new StreamSiteId(StreamProgram.Dispose, StreamPhase.DetachSource);
        if (!schedule.Sites.Any(s => s.Id == cleanup) ||
            !schedule.Awaits.Any(a => a.Producer == new StreamSiteId(StreamProgram.Dispose, StreamPhase.DisposeSource)) ||
            !schedule.Owners.Any(o => o.Id == "source" && o.Cleanup == StreamCleanup.AsyncDispose && !o.Borrowed)) return null;
        var id = CanonicalSplitSourceEmitter.Id(composition.Identity + "|Stream|" + route.Request);
        var lowering = CanonicalStreamPrototypeLowering.Create(route, schedule);
        if (lowering == null) return null;
        return new(composition.GeneratedNamespace, "__ZendiatorStreamPrototype_" + id,
            "Zendiator.StreamPrototype." + id + ".g.cs", schedule, cleanup, call,
            new(route.Stages.Select(s => s.StreamEmission!)), lowering.Value.Item, lowering.Value.Cleanup) { Generic = route.StreamGeneric };
    }
}
