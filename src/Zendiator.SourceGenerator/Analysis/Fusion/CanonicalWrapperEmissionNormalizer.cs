using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class CanonicalWrapperEmissionNormalizer
{
    internal static CanonicalWrapperEmissionModel Create(CompositionSelectionCatalog catalog, CanonicalExecutionGraphCatalog execution)
    {
        var output = new List<CanonicalWrapperCompositionEmission>();
        for (var i = 0; i < catalog.Compositions.Count; i++)
        {
            var c = catalog.Compositions[i];
            if (c.GeneratedNamespace == null || c.Errors.Count != 0 || c.CompilerOwnsHandlerError || c.Pipelines.Count != 1 ||
                c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" || catalog.Compositions.Count(x => x.Identity == c.Identity || x.Key == c.Key || x.GeneratedNamespace == c.GeneratedNamespace) != 1) continue;
            var route = c.Pipelines[0]; var graph = execution.Compositions[i].SingleOrDefault(g => g.Contract.Anchor == route.Anchor);
            if (graph?.IsValid != true || graph.Schedule == null || route.Family is not (BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync) ||
                route.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void) || route.Handlers.Count != 1 || !route.ExplicitHandlerOrder) continue;
            var wrappers = route.Stages.Select((s, index) => (Stage: s, Index: index)).Where(s => s.Stage.Kind == FusionStageKind.Wrapper).ToArray();
            if (wrappers.Length == 0) continue; var w = wrappers[0];
            var sync = route.Family == BoundDispatchFamily.SendSync; var h = route.Handlers[0];
            if (h.CompilerOwnsError || !c.Handlers.Any(x => x.Identity == h.Type) || sync && h.SyncEmission == null || !sync && h.AsyncEmission == null) continue;
            var regions = new List<CanonicalWrapperRegionEmission>(); var pending = new List<HookCapabilityEmission>(); var invalid = false;
            foreach (var s in route.Stages)
            {
                if (s.CompilerOwnsError || s.Sessions.Count > 1) { invalid = true; break; }
                if (s.Kind == FusionStageKind.Hook) { if (s.Capabilities == null) { invalid = true; break; } var hf = s.Sessions.Count == 0 ? null : CanonicalCapabilityEmissionNormalizer.Factory(s.Sessions[0], sync); if (s.Sessions.Count == 0 ? s.ServiceOwnership != FusionOwnership.BorrowedService : hf == null || s.ServiceOwnership != s.Sessions[0].Ownership) { invalid = true; break; } pending.Add(hf == null ? s.Capabilities : s.Capabilities with { Factory = hf }); continue; }
                if (s.Kind != FusionStageKind.Wrapper || s.WrapperEmission == null || s.WrapperEmission.Completion != (sync ? BoundCompletionKind.Synchronous : BoundCompletionKind.ValueTask)) { invalid = true; break; }
                var factory = s.Sessions.Count == 0 ? null : CanonicalCapabilityEmissionNormalizer.Factory(s.Sessions[0], sync);
                if (s.Sessions.Count == 0 ? s.ServiceOwnership != FusionOwnership.BorrowedService : factory == null || s.ServiceOwnership != s.Sessions[0].Ownership) { invalid = true; break; }
                regions.Add(Region(s.WrapperEmission, factory)); pending.Clear();
            }
            if (invalid || h.Sessions.Count > 1) continue; regions.Add(Region(null, null));
            CanonicalWrapperRegionEmission Region(BoundWrapperEmission? wrapper, BoundAsyncFactoryEmission? factory) => pending.Count <= 1 && pending.All(x => x.Factory == null) ? new(pending.SingleOrDefault(), wrapper, factory) : new(null, wrapper, factory) { Hooks = new(pending) };
            var generalized = wrappers.Length != 1 || w.Stage.Sessions.Count != 0 || regions.Any(r => r.Hooks.Count != 0);
            var outer = !generalized && w.Index == 1 ? route.Stages[0] : null; var inner = !generalized && w.Index + 1 < route.Stages.Count ? route.Stages[w.Index + 1] : null;
            BoundSyncFactoryEmission? sf = null; BoundAsyncFactoryEmission? af = null;
            if (h.Sessions.Count == 1)
            {
                var s = h.Sessions[0];
                if (!s.SuppliesCallbackReceiver || s.Ownership is not (FusionOwnership.OwnedSession or FusionOwnership.BorrowedResource)) continue;
                if (sync)
                {
                    sf = s.SyncEmission;
                    if (sf is null || s.Acquire.Completion != BoundCompletionKind.Synchronous ||
                        s.Ownership == FusionOwnership.OwnedSession && (sf.CleanupContractName == null || s.Cleanup?.Completion != BoundCompletionKind.Synchronous) ||
                        s.Ownership == FusionOwnership.BorrowedResource && (sf.CleanupContractName != null || s.Cleanup != null)) continue;
                }
                else
                {
                    af = s.AsyncEmission;
                    if (af is null || s.Acquire.Completion is not (BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask) ||
                        s.Ownership == FusionOwnership.OwnedSession && (af.CleanupContractName == null || s.Cleanup?.Completion != BoundCompletionKind.ValueTask) ||
                        s.Ownership == FusionOwnership.BorrowedResource && (af.CleanupContractName != null || s.Cleanup != null)) continue;
                    sf = new(af.FactoryName, af.ContractName, af.MethodName, af.CleanupContractName) { FactoryIsValueType = af.FactoryIsValueType };
                }
            }
            else if (h.ServiceOwnership != FusionOwnership.BorrowedService) continue;
            var handler = sync ? h.SyncEmission! : new BoundSyncHandlerEmission(h.AsyncEmission!.RequestName, h.AsyncEmission.HandlerName, h.AsyncEmission.ContractName, h.AsyncEmission.MethodName, h.AsyncEmission.ResultName) { RequestIsValueType = h.AsyncEmission.RequestIsValueType, HandlerIsValueType = h.AsyncEmission.HandlerIsValueType };
            var registration = new CanonicalSyncCompositionEmission(i, c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers, handler, sf, false, route.PipelineFamily, c.MicrosoftDiProviderName ?? "")
            { ExplicitHandlerTypes = c.ExplicitHandlerTypes, CatalogMarkerTypes = c.CatalogMarkerTypes, ExternAliases = c.ExternAliases, RegistrationStages = new(route.Stages.Select(s => new RegistrationStageEmission(s.Name, s.Kind == FusionStageKind.Wrapper ? "Wrapper" : "Hook", s.Sessions.Count == 0 ? null : CanonicalCapabilityEmissionNormalizer.Factory(s.Sessions[0], sync)!.FactoryName))) };
            output.Add(new(registration, route.Family.Value, w.Stage.WrapperEmission!, outer?.SyncEmission, inner?.SyncEmission, outer?.AsyncEmission, inner?.AsyncEmission, af)
            {
                OuterCapabilities = outer != null && (sync ? outer.SyncEmission == null : outer.AsyncEmission == null) ? outer.Capabilities : null,
                InnerCapabilities = inner != null && (sync ? inner.SyncEmission == null : inner.AsyncEmission == null) ? inner.Capabilities : null,
                Regions = generalized ? new(regions) : default
            });
        }
        return new(new(output));
    }
    // The All graph is proved before projecting each branch to the accepted Single backend.
    // Keep the current All boundary explicit; Single also supports wider wrapper/value shapes.
    internal static EquatableArray<CanonicalWrapperCompositionEmission> AllBranches(CompositionSelectionModel composition,
        BoundPipelineModel route, CanonicalExecutionGraphModel graph, int index)
    {
        var sync = route.Family == BoundDispatchFamily.SendAllSync;
        if (route.Family is not (BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync) ||
            route.RequestShape is not (BoundRequestShape.ClosedReference or BoundRequestShape.ClosedNonGenericValue) ||
            route.Stages.Count(s => s.Kind == FusionStageKind.Wrapper) != 1 ||
            route.Stages.Any(s => s.CompilerOwnsError || !Session(s.Sessions, s.ServiceOwnership) ||
                (s.Kind == FusionStageKind.Wrapper ? s.WrapperEmission is not { WrapperIsValueType: false } :
                    s.Kind != FusionStageKind.Hook || s.Capabilities is not { HookIsValueType: false })) ||
            route.Handlers.Any(h => h.CompilerOwnsError || !Session(h.Sessions, h.ServiceOwnership) ||
                (sync ? h.SyncEmission is not { HandlerIsValueType: false } || h.SyncEmission.RequestIsValueType != (route.RequestShape == BoundRequestShape.ClosedNonGenericValue) ||
                        h.SyncEmission.ResultName is not (null or "int" or "global::System.Int32") :
                    h.AsyncEmission is not { HandlerIsValueType: false } || h.AsyncEmission.RequestIsValueType != (route.RequestShape == BoundRequestShape.ClosedNonGenericValue) ||
                        h.AsyncEmission.ResultName is not (null or "int" or "global::System.Int32")))) return default;
        if (route.RequestIsGeneric && (route.RequestShape != BoundRequestShape.ClosedReference ||
            !route.Stages.Any(s => s.Kind == FusionStageKind.Hook) ||
            route.Stages.Any(s => s.ReceiverIsGeneric || s.Sessions.Any(f => f.FactoryIsGeneric) ||
                s.Kind == FusionStageKind.Hook && (sync ? s.SyncEmission is not { HookIsValueType: false } : s.AsyncEmission is not { HookIsValueType: false })) ||
            route.Handlers.Any(h => h.ReceiverIsGeneric && !h.ReceiverIsPublic || h.Sessions.Any(f => f.FactoryIsGeneric)))) return default;
        bool Session(EquatableArray<BoundSessionModel> sessions, FusionOwnership ownership) => sessions.Count == 0
            ? ownership == FusionOwnership.BorrowedService
            : sessions.Count == 1 && ownership == sessions[0].Ownership &&
                CanonicalCapabilityEmissionNormalizer.Factory(sessions[0], sync) is { FactoryIsValueType: false };
        var branches = new List<CanonicalWrapperCompositionEmission>();
        foreach (var handler in route.Handlers)
        {
            var projected = composition with
            {
                Pipelines = new(new[] { route with {
                Family = sync ? BoundDispatchFamily.SendSync : BoundDispatchFamily.SendAsync,
                Handlers = new(new[] { handler }) } })
            };
            var branch = Create(new(new(new[] { projected })),
                new(new(new[] { new EquatableArray<CanonicalExecutionGraphModel>(new[] { graph }) }))).Compositions.SingleOrDefault();
            if (branch == null) return default;
            branches.Add(branch with { Registration = branch.Registration with { Index = index } });
        }
        return new(branches);
    }
    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalWrapperEmissionModel model, ImmutableArray<CompositionSelectionDiscovery.Binding> current, Compilation compilation, CancellationToken token) =>
        CanonicalSyncEmissionNormalizer.BindCallSites(new(new(model.Compositions.Select(c => c.Registration))), current, compilation, token);
}
