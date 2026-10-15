using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class CanonicalSendAllAsyncEmissionNormalizer
{
    internal static CanonicalSendAllAsyncEmissionModel Create(CompositionSelectionCatalog catalog, CanonicalExecutionGraphCatalog execution)
    {
        var result = new List<CanonicalSendAllAsyncCompositionEmission>();
        for (var i = 0; i < catalog.Compositions.Count; i++)
        {
            var c = catalog.Compositions[i];
            if (c.GeneratedNamespace == null || c.MarkerName == null || c.Errors.Count != 0 || c.CompilerOwnsHandlerError ||
                c.ExternAliases.Count != 0 || c.Pipelines.Count != 1 || c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" ||
                catalog.Compositions.Count(x => x.Identity == c.Identity || x.Key == c.Key || x.GeneratedNamespace == c.GeneratedNamespace) != 1) continue;
            var r = c.Pipelines[0];
            var graph = execution.Compositions[i].SingleOrDefault(g => g.Contract.Anchor == r.Anchor);
            if (graph?.IsValid != true || graph.Schedule == null || r.Family != BoundDispatchFamily.SendAllAsync ||
                r.RequestShape is not (BoundRequestShape.ClosedReference or BoundRequestShape.ClosedNonGenericValue) || !r.ExplicitHandlerOrder || r.Handlers.Count == 0 ||
                r.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void)) continue;
            if (r.RequestIsGeneric && (r.RequestShape != BoundRequestShape.ClosedReference ||
                r.Stages.Any(s => s.Sessions.Any(f => f.FactoryIsGeneric)) ||
                r.Handlers.Any(h => h.Sessions.Any(f => f.FactoryIsGeneric)))) continue;
            if (r.Stages.Any(s => s.Kind == FusionStageKind.Wrapper))
            {
                var wrappers = CanonicalWrapperEmissionNormalizer.AllBranches(c, r, graph, i);
                if (wrappers.Count == r.Handlers.Count)
                    result.Add(new(new(wrappers.Select((w, branch) => new CanonicalAsyncCompositionEmission(i,
                        c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers, r.Handlers[branch].AsyncEmission!, w.AsyncFactory,
                        false, r.PipelineFamily, c.MicrosoftDiProviderName ?? ""))))
                    { Wrappers = wrappers });
                continue;
            }
            if (r.Stages.Count != 1) continue;
            var stage = r.Stages[0];
            if (stage.CompilerOwnsError || stage.Kind != FusionStageKind.Hook || stage.AsyncEmission is not { HookIsValueType: false } hook ||
                !TryFactory(stage.Sessions, stage.ServiceOwnership, out var hookFactory)) continue;
            if (r.RequestIsGeneric && (stage.ReceiverIsGeneric && !stage.ReceiverIsPublic ||
                r.Handlers.Any(h => h.ReceiverIsGeneric && !h.ReceiverIsPublic))) continue;
            var branches = new List<CanonicalAsyncCompositionEmission>();
            foreach (var h in r.Handlers)
            {
                if (h.CompilerOwnsError || h.Callback?.Completion != BoundCompletionKind.ValueTask ||
                    h.AsyncEmission is not { HandlerIsValueType: false } || h.AsyncEmission.RequestIsValueType != (r.RequestShape == BoundRequestShape.ClosedNonGenericValue) ||
                    h.AsyncEmission.ResultName is not (null or "int" or "global::System.Int32") || !c.Handlers.Any(m => m.Identity == h.Type) ||
                    !TryFactory(h.Sessions, h.ServiceOwnership, out var factory)) break;
                branches.Add(new(i, c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers, h.AsyncEmission, factory,
                    false, r.PipelineFamily, c.MicrosoftDiProviderName ?? "")
                {
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

    private static bool TryFactory(EquatableArray<BoundSessionModel> sessions, FusionOwnership ownership, out BoundAsyncFactoryEmission? factory)
    {
        factory = null;
        if (sessions.Count == 0) return ownership == FusionOwnership.BorrowedService;
        if (sessions.Count != 1 || ownership != sessions[0].Ownership) return false;
        factory = CanonicalAsyncEmissionNormalizer.AsyncFactory(sessions[0]);
        return factory is { FactoryIsValueType: false };
    }

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalSendAllAsyncEmissionModel model,
        ImmutableArray<CompositionSelectionDiscovery.Binding> current, Compilation compilation, CancellationToken ct) =>
        CanonicalSyncEmissionNormalizer.BindCallSites(new(new(model.Compositions.Select(c => c.Registration))), current, compilation, ct);

    internal static CanonicalSendAllAsyncEmissionModel Connected(CanonicalSendAllAsyncEmissionModel model, CanonicalSyncInterceptorModel sites) =>
        new(new(model.Compositions.Where(c => sites.CallSites.Count(s => s.Namespace == c.Branch.Namespace && s.MarkerName == c.Branch.MarkerName) == 1)));
}
