using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class CanonicalSendAllSyncEmissionNormalizer
{
    internal static CanonicalSendAllSyncEmissionModel Create(CompositionSelectionCatalog catalog, CanonicalExecutionGraphCatalog execution)
    {
        var result = new List<CanonicalSendAllSyncCompositionEmission>();
        for (var i = 0; i < catalog.Compositions.Count; i++)
        {
            var c = catalog.Compositions[i];
            if (c.GeneratedNamespace == null || c.MarkerName == null || c.Errors.Count != 0 || c.CompilerOwnsHandlerError ||
                c.ExternAliases.Count != 0 || c.Pipelines.Count != 1 || c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" ||
                catalog.Compositions.Count(x => x.Identity == c.Identity || x.Key == c.Key || x.GeneratedNamespace == c.GeneratedNamespace) != 1) continue;
            var r = c.Pipelines[0];
            var graph = execution.Compositions[i].SingleOrDefault(g => g.Contract.Anchor == r.Anchor);
            if (graph?.IsValid != true || graph.Schedule == null || r.Family != BoundDispatchFamily.SendAllSync ||
                r.RequestShape is not (BoundRequestShape.ClosedReference or BoundRequestShape.ClosedNonGenericValue or BoundRequestShape.OpenAllSyncReference) || !r.ExplicitHandlerOrder || r.Handlers.Count == 0 ||
                r.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void)) continue;
            if (r.RequestShape == BoundRequestShape.OpenAllSyncReference &&
                (r.SendGeneric == null || r.SendOpenRegistration is not { } open || open.HandlerDefinitionNames.Count != 2 ||
                r.Handlers.Count != 2 || r.ResultKind != FusionResultKind.Response || r.Stages.Count != 1 || open.Stages.Count != 1 ||
                r.Handlers.Any(h => h.Sessions.Count != 0 || h.SyncEmission is not { HandlerIsValueType: false, RequestIsValueType: false }) ||
                r.Stages[0].Kind != FusionStageKind.Hook || r.Stages[0].Sessions.Count != 0 ||
                r.Stages[0].SyncEmission is not { HookIsValueType: false } || c.Handlers.Any(h => !open.HandlerMembership.Contains(h.Identity)))) continue;
            if (r.RequestIsGeneric && (r.RequestShape is not (BoundRequestShape.ClosedReference or BoundRequestShape.OpenAllSyncReference) ||
                r.Stages.Any(s => s.Sessions.Any(f => f.FactoryIsGeneric && !f.FactoryIsPublic)) ||
                r.Handlers.Any(h => h.Sessions.Any(f => f.FactoryIsGeneric && !f.FactoryIsPublic)))) continue;
            if (r.Stages.Any(s => s.Kind == FusionStageKind.Wrapper))
            {
                var wrappers = CanonicalWrapperEmissionNormalizer.AllBranches(c, r, graph, i);
                if (wrappers.Count == r.Handlers.Count)
                    result.Add(new(new(wrappers.Select(w => w.Registration))) { Wrappers = wrappers });
                continue;
            }
            if (r.Stages.Count != 1) continue;
            var stage = r.Stages[0];
            if (stage.CompilerOwnsError || stage.Kind != FusionStageKind.Hook || stage.SyncEmission is not { HookIsValueType: false } hook ||
                !TryFactory(stage.Sessions, stage.ServiceOwnership, out var hookFactory)) continue;
            if (r.RequestIsGeneric && (stage.ReceiverIsGeneric && !stage.ReceiverIsPublic ||
                r.Handlers.Any(h => h.ReceiverIsGeneric && !h.ReceiverIsPublic))) continue;
            var branches = new List<CanonicalSyncCompositionEmission>();
            foreach (var h in r.Handlers)
            {
                if (h.CompilerOwnsError || h.Callback?.Completion != BoundCompletionKind.Synchronous ||
                    h.SyncEmission is not { HandlerIsValueType: false } || h.SyncEmission.RequestIsValueType != (r.RequestShape == BoundRequestShape.ClosedNonGenericValue) ||
                    h.SyncEmission.ResultName is not (null or "int" or "global::System.Int32") || !c.Handlers.Any(m => m.Identity == h.Type) ||
                    !TryFactory(h.Sessions, h.ServiceOwnership, out var factory)) break;
                branches.Add(new(i, c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers, h.SyncEmission, factory,
                    false, r.PipelineFamily, c.MicrosoftDiProviderName ?? "")
                {
                    Generic = r.SendGeneric,
                    OpenRegistration = r.SendOpenRegistration,
                    Hooks = new(new[] { hookFactory == null ? hook : hook with { Factory = hookFactory } }),
                    ExplicitHandlerTypes = c.ExplicitHandlerTypes,
                    CatalogMarkerTypes = c.CatalogMarkerTypes,
                    ExternAliases = c.ExternAliases
                });
            }
            if (branches.Count == r.Handlers.Count) result.Add(new(new(branches)));
        }
        return new(new(result));
    }

    private static bool TryFactory(EquatableArray<BoundSessionModel> sessions, FusionOwnership ownership, out BoundSyncFactoryEmission? factory)
    {
        factory = null;
        if (sessions.Count == 0) return ownership == FusionOwnership.BorrowedService;
        if (sessions.Count != 1 || ownership != sessions[0].Ownership) return false;
        factory = CanonicalSyncEmissionNormalizer.SyncFactory(sessions[0]);
        return factory is { FactoryIsValueType: false };
    }

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalSendAllSyncEmissionModel model,
        ImmutableArray<CompositionSelectionDiscovery.Binding> current, Compilation compilation, CancellationToken ct) =>
        CanonicalSyncEmissionNormalizer.BindCallSites(new(new(model.Compositions.Select(c => c.Registration))), current, compilation, ct);

    internal static CanonicalSendAllSyncEmissionModel Connected(CanonicalSendAllSyncEmissionModel model, CanonicalSyncInterceptorModel sites) =>
        new(new(model.Compositions.Where(c => sites.CallSites.Count(s => s.Namespace == c.Registration.Namespace && s.MarkerName == c.Registration.MarkerName) == 1)));
}
