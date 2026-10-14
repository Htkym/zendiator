using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class CanonicalAsyncEmissionNormalizer
{
    internal static CanonicalAsyncEmissionModel Create(CompositionSelectionCatalog catalog, CanonicalExecutionGraphCatalog execution)
    {
        var output = new List<CanonicalAsyncCompositionEmission>();
        for (var i = 0; i < catalog.Compositions.Count; i++)
        {
            var c = catalog.Compositions[i];
            if (c.GeneratedNamespace == null || c.Errors.Count != 0 || c.CompilerOwnsHandlerError ||
                c.Pipelines.Count != 1 || c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" ||
                catalog.Compositions.Count(other => other.Identity == c.Identity || other.Key == c.Key || other.GeneratedNamespace == c.GeneratedNamespace) != 1) continue;
            var route = c.Pipelines[0];
            var graph = execution.Compositions[i].SingleOrDefault(g => g.Contract.Anchor == route.Anchor);
            if (graph?.IsValid != true || graph.Schedule == null || route.Family != BoundDispatchFamily.SendAsync || route.Stages.Count > 2 ||
                route.Handlers.Count != 1 || !route.ExplicitHandlerOrder || route.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void)) continue;
            var h = route.Handlers[0];
            if (h.CompilerOwnsError || h.AsyncEmission == null || h.Callback?.Completion != BoundCompletionKind.ValueTask ||
                !c.Handlers.Any(handler => handler.Identity == h.Type)) continue;
            BoundAsyncFactoryEmission? factory = null;
            var hooks = new List<BoundAsyncHookEmission>();
            if (route.Stages.Count != 0)
            {
                // A Handler session is inside one Hook's observation and outer cleanup boundary.
                if (h.Sessions.Count != 0 && route.Stages.Count != 1 ||
                    h.Sessions.Count == 0 && h.ServiceOwnership != FusionOwnership.BorrowedService ||
                    route.Stages.Any(s => s.CompilerOwnsError || s.Kind != FusionStageKind.Hook || s.AsyncEmission == null)) continue;
                if (route.Stages.Any(s => s.Sessions.Count != 0))
                {
                    var stage = route.Stages[0];
                    if (route.Stages.Count != 1 || stage.Sessions.Count != 1 || stage.ServiceOwnership != stage.Sessions[0].Ownership ||
                        AsyncFactory(stage.Sessions[0]) is not { } hookFactory) continue;
                    hooks.Add(stage.AsyncEmission! with { Factory = hookFactory });
                }
                else
                {
                    if (route.Stages.Any(s => s.ServiceOwnership != FusionOwnership.BorrowedService)) continue;
                    hooks.AddRange(route.Stages.Select(s => s.AsyncEmission!));
                }
            }
            if (h.Sessions.Count != 0)
            {
                if (h.Sessions.Count != 1) continue;
                var session = h.Sessions[0];
                factory = AsyncFactory(session);
                if (factory is null) continue;
            }
            else if (h.ServiceOwnership != FusionOwnership.BorrowedService) continue;
            output.Add(new(i, c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers, h.AsyncEmission, factory,
                route.HasEmptyPipeline, route.PipelineFamily, c.MicrosoftDiProviderName ?? "")
            { Hooks = new(hooks), ExplicitHandlerTypes = c.ExplicitHandlerTypes, CatalogMarkerTypes = c.CatalogMarkerTypes, ExternAliases = c.ExternAliases });
        }
        return new(new(output));
    }

    private static BoundAsyncFactoryEmission? AsyncFactory(BoundSessionModel session)
    {
        if (!session.SuppliesCallbackReceiver || session.AsyncEmission is not { } bound ||
            session.Acquire.Completion is not (BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask) ||
            session.Ownership is not (FusionOwnership.OwnedSession or FusionOwnership.BorrowedResource) ||
            session.Ownership == FusionOwnership.OwnedSession && (session.Cleanup?.Completion != BoundCompletionKind.ValueTask || bound.CleanupContractName == null) ||
            session.Ownership == FusionOwnership.BorrowedResource && (session.Cleanup != null || bound.CleanupContractName != null)) return null;
        return bound;
    }

    // Registration and call-site binding only need callable names; no synchronous body is reused.
    internal static CanonicalSyncCompositionEmission Registration(CanonicalAsyncCompositionEmission c) => new(c.Index, c.Namespace, c.MarkerName, c.UseLocalHandlers,
        new(c.Handler.RequestName, c.Handler.HandlerName, c.Handler.ContractName, c.Handler.MethodName, c.Handler.ResultName) { RequestIsValueType = c.Handler.RequestIsValueType, HandlerIsValueType = c.Handler.HandlerIsValueType },
        c.Factory == null ? null : new(c.Factory.FactoryName, c.Factory.ContractName, c.Factory.MethodName, c.Factory.CleanupContractName) { FactoryIsValueType = c.Factory.FactoryIsValueType },
        c.HasEmptyPipeline, c.PipelineFamily, c.MicrosoftDiProviderName ?? "")
    {
        ExplicitHandlerTypes = c.ExplicitHandlerTypes,
        CatalogMarkerTypes = c.CatalogMarkerTypes,
        ExternAliases = c.ExternAliases,
        Hooks = new(c.Hooks.Select(h => new BoundSyncHookEmission(h.HookName,
            new(h.Before.ContractName, h.Before.MethodName), new(h.Success.ContractName, h.Success.MethodName),
            new(h.Exception.ContractName, h.Exception.MethodName), new(h.Finally.ContractName, h.Finally.MethodName))
        { HookIsValueType = h.HookIsValueType, Factory = h.Factory == null ? null : new(h.Factory.FactoryName, h.Factory.ContractName, h.Factory.MethodName, h.Factory.CleanupContractName) { FactoryIsValueType = h.Factory.FactoryIsValueType } }))
    };

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalAsyncEmissionModel model, ImmutableArray<CompositionSelectionDiscovery.Binding> current,
        Compilation compilation, CancellationToken token) => CanonicalSyncEmissionNormalizer.BindCallSites(
            new(new(model.Compositions.Select(Registration))), current, compilation, token);
}
