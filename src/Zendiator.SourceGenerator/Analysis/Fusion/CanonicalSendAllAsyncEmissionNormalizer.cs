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
                r.RequestShape != BoundRequestShape.ClosedReference || r.RequestIsGeneric || !r.ExplicitHandlerOrder || r.Handlers.Count == 0 ||
                r.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void) || r.Stages.Count != 1) continue;
            var stage = r.Stages[0];
            if (stage.CompilerOwnsError || stage.Kind != FusionStageKind.Hook || stage.AsyncEmission is not { HookIsValueType: false } hook ||
                stage.ServiceOwnership != FusionOwnership.BorrowedService || stage.Sessions.Count != 0 || hook.Factory != null ||
                r.Handlers.Any(h => h.CompilerOwnsError || h.Callback?.Completion != BoundCompletionKind.ValueTask || h.Sessions.Count != 0 ||
                    h.ServiceOwnership != FusionOwnership.BorrowedService || h.AsyncEmission is not { RequestIsValueType: false, HandlerIsValueType: false } ||
                    h.AsyncEmission.ResultName is not (null or "int" or "global::System.Int32") || !c.Handlers.Any(m => m.Identity == h.Type))) continue;
            var branch = new CanonicalAsyncCompositionEmission(i, c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers,
                r.Handlers[0].AsyncEmission!, null, false, r.PipelineFamily, c.MicrosoftDiProviderName ?? "")
            { Hooks = new(new[] { hook }), ExplicitHandlerTypes = c.ExplicitHandlerTypes, CatalogMarkerTypes = c.CatalogMarkerTypes, ExternAliases = c.ExternAliases };
            result.Add(new(branch, new(r.Handlers.Select(h => h.AsyncEmission!))));
        }
        return new(new(result));
    }

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalSendAllAsyncEmissionModel model,
        ImmutableArray<CompositionSelectionDiscovery.Binding> current, Compilation compilation, CancellationToken ct) =>
        CanonicalSyncEmissionNormalizer.BindCallSites(new(new(model.Compositions.Select(c => c.Registration))), current, compilation, ct);

    internal static CanonicalSendAllAsyncEmissionModel Connected(CanonicalSendAllAsyncEmissionModel model, CanonicalSyncInterceptorModel sites) =>
        new(new(model.Compositions.Where(c => sites.CallSites.Count(s => s.Namespace == c.Branch.Namespace && s.MarkerName == c.Branch.MarkerName) == 1)));
}
