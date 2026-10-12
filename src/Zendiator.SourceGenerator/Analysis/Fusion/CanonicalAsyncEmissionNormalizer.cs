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
            if (c.GeneratedNamespace == null || c.MicrosoftDiProviderName == null || c.Errors.Count != 0 || c.CompilerOwnsHandlerError ||
                c.HasExplicitSelection || c.Pipelines.Count != 1 || c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" ||
                catalog.Compositions.Count(other => other.Identity == c.Identity || other.Key == c.Key || other.GeneratedNamespace == c.GeneratedNamespace) != 1) continue;
            var route = c.Pipelines[0];
            var graph = execution.Compositions[i].SingleOrDefault(g => g.Contract.Anchor == route.Anchor);
            if (graph?.IsValid != true || graph.Schedule == null || route.Family != BoundDispatchFamily.SendAsync || route.Stages.Count != 0 ||
                route.Handlers.Count != 1 || !route.ExplicitHandlerOrder || route.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void)) continue;
            var h = route.Handlers[0];
            if (h.CompilerOwnsError || h.AsyncEmission == null || h.Callback?.Completion != BoundCompletionKind.ValueTask ||
                !c.Handlers.Any(handler => handler.Identity == h.Type)) continue;
            BoundAsyncFactoryEmission? factory = null;
            if (h.Sessions.Count != 0)
            {
                if (h.Sessions.Count != 1) continue;
                var session = h.Sessions[0];
                if (!session.SuppliesCallbackReceiver || session.AsyncEmission is not { IsSourceDeclaration: true } bound ||
                    session.Acquire.Completion is not (BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask) ||
                    session.Ownership is not (FusionOwnership.OwnedSession or FusionOwnership.BorrowedResource) ||
                    session.Ownership == FusionOwnership.OwnedSession && (session.Cleanup?.Completion != BoundCompletionKind.ValueTask || bound.CleanupContractName == null) ||
                    session.Ownership == FusionOwnership.BorrowedResource && (session.Cleanup != null || bound.CleanupContractName != null)) continue;
                factory = bound;
            }
            else if (h.ServiceOwnership != FusionOwnership.BorrowedService) continue;
            output.Add(new(i, c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers, h.AsyncEmission, factory,
                route.HasEmptyPipeline, route.PipelineFamily, c.MicrosoftDiProviderName));
        }
        return new(new(output));
    }

    // Registration and call-site binding only need callable names; no synchronous body is reused.
    internal static CanonicalSyncCompositionEmission Registration(CanonicalAsyncCompositionEmission c) => new(c.Index, c.Namespace, c.MarkerName, c.UseLocalHandlers,
        new(c.Handler.RequestName, c.Handler.HandlerName, c.Handler.ContractName, c.Handler.MethodName, c.Handler.ResultName),
        c.Factory == null ? null : new(c.Factory.FactoryName, c.Factory.ContractName, c.Factory.MethodName, c.Factory.CleanupContractName),
        c.HasEmptyPipeline, c.PipelineFamily, c.MicrosoftDiProviderName);

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalAsyncEmissionModel model, ImmutableArray<CompositionSelectionDiscovery.Binding> current,
        Compilation compilation, CancellationToken token) => CanonicalSyncEmissionNormalizer.BindCallSites(
            new(new(model.Compositions.Select(Registration))), current, compilation, token);
}
