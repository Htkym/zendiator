using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Zendiator.SourceGenerator;

/// <summary>Limits the first runtime candidate to the matrix whose execution tests can prove its behavior.</summary>
internal static class CanonicalSyncEmissionNormalizer
{
    internal static CanonicalSyncEmissionModel Create(CompositionSelectionCatalog catalog, CanonicalExecutionGraphCatalog execution)
    {
        var output = new List<CanonicalSyncCompositionEmission>();
        for (var i = 0; i < catalog.Compositions.Count; i++)
        {
            var composition = catalog.Compositions[i];
            if (composition.GeneratedNamespace == null || composition.Errors.Count != 0 || composition.CompilerOwnsHandlerError ||
                composition.Pipelines.Count != 1 ||
                composition.Facts.Count != 1 || composition.Facts[0].Key != "IncompleteExecutionContract" ||
                catalog.Compositions.Count(c => c.Identity == composition.Identity || c.Key == composition.Key ||
                    c.GeneratedNamespace == composition.GeneratedNamespace) != 1) continue;
            var route = composition.Pipelines[0];
            var graph = execution.Compositions[i].SingleOrDefault(g => g.Contract.Anchor == route.Anchor);
            if (graph?.IsValid != true || graph.Schedule == null || route.Family != BoundDispatchFamily.SendSync ||
                route.Stages.Count > 2 || route.Handlers.Count != 1 || !route.ExplicitHandlerOrder ||
                route.ResultKind is not (FusionResultKind.Void or FusionResultKind.Response)) continue;
            var handler = route.Handlers[0];
            // Default membership may contain an open definition and its explicitly closed route.
            // Only the authoritative route participates in lowering or DI registration.
            if (handler.CompilerOwnsError || handler.SyncEmission == null || !composition.Handlers.Any(h => h.Identity == handler.Type)) continue;
            var hooks = new List<BoundSyncHookEmission>();
            if (route.Stages.Count != 0)
            {
                if ((handler.Sessions.Count != 0 && route.Stages.Count != 1) || route.Stages.Any(stage => stage.CompilerOwnsError ||
                    stage.Kind != FusionStageKind.Hook || stage.SyncEmission == null)) continue;
                if (route.Stages.Any(stage => stage.Sessions.Count != 0))
                {
                    // One session Hook; a Handler session stays inside its observation and cleanup boundary.
                    var stage = route.Stages[0];
                    if (route.Stages.Count != 1 || stage.Sessions.Count != 1 ||
                        stage.ServiceOwnership != stage.Sessions[0].Ownership ||
                        SyncFactory(stage.Sessions[0]) is not { } hookFactory) continue;
                    hooks.Add(stage.SyncEmission! with { Factory = hookFactory });
                }
                else
                {
                    if (route.Stages.Any(stage => stage.ServiceOwnership != FusionOwnership.BorrowedService)) continue;
                    hooks.AddRange(route.Stages.Select(stage => stage.SyncEmission!));
                }
            }
            BoundSyncFactoryEmission? factory = null;
            if (handler.Sessions.Count != 0)
            {
                if (handler.Sessions.Count != 1) continue;
                factory = SyncFactory(handler.Sessions[0]);
                if (factory == null) continue;
            }
            else if (handler.ServiceOwnership != FusionOwnership.BorrowedService) continue;
            output.Add(new(i, composition.GeneratedNamespace, composition.MarkerName, composition.UseLocalHandlers,
                handler.SyncEmission, factory, route.HasEmptyPipeline, route.PipelineFamily, composition.MicrosoftDiProviderName ?? "")
            { Hooks = new(hooks), ExplicitHandlerTypes = composition.ExplicitHandlerTypes, CatalogMarkerTypes = composition.CatalogMarkerTypes, ExternAliases = composition.ExternAliases });
        }
        return new(new(output));
    }

    private static BoundSyncFactoryEmission? SyncFactory(BoundSessionModel session)
    {
        if (!session.SuppliesCallbackReceiver || session.SyncEmission == null || session.Acquire.Completion != BoundCompletionKind.Synchronous ||
            session.Ownership is not (FusionOwnership.OwnedSession or FusionOwnership.BorrowedResource) ||
            session.Ownership == FusionOwnership.OwnedSession && (session.Cleanup?.Completion != BoundCompletionKind.Synchronous ||
                session.SyncEmission.CleanupContractName == null) ||
            session.Ownership == FusionOwnership.BorrowedResource && (session.Cleanup != null || session.SyncEmission.CleanupContractName != null)) return null;
        return session.SyncEmission;
    }

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalSyncEmissionModel model,
        ImmutableArray<CompositionSelectionDiscovery.Binding> current, Compilation compilation, CancellationToken ct)
    {
        var output = new List<CanonicalSyncCallSite>();
        foreach (var composition in model.Compositions)
        {
            ct.ThrowIfCancellationRequested();
            var invocation = current[composition.Index].Invocation;
            var semantic = compilation.GetSemanticModel(invocation.SyntaxTree);
            var location = semantic.GetInterceptableLocation(invocation, ct);
            if (location != null && semantic.GetSymbolInfo(invocation, ct).Symbol is IMethodSymbol method)
                output.Add(new(composition.Namespace, composition.MarkerName, location.Version, location.Data, method.ReducedFrom != null) { ExternAliases = composition.ExternAliases });
        }
        return new(new(output));
    }
}
