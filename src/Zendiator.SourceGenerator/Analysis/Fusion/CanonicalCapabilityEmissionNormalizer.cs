using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace Zendiator.SourceGenerator;

internal static class CanonicalCapabilityEmissionNormalizer
{
    internal static CanonicalCapabilityEmissionModel Create(CompositionSelectionCatalog catalog, CanonicalExecutionGraphCatalog execution,
        CanonicalSyncEmissionModel syncModel, CanonicalAsyncEmissionModel asyncModel, CanonicalWrapperEmissionModel wrapperModel)
    {
        var output = new List<CanonicalCapabilityCompositionEmission>();
        for (var i = 0; i < catalog.Compositions.Count; i++)
        {
            if (syncModel.Compositions.Any(c => c.Index == i) || asyncModel.Compositions.Any(c => c.Index == i) || wrapperModel.Compositions.Any(c => c.Registration.Index == i))
                continue;
            var c = catalog.Compositions[i];
            if (c.GeneratedNamespace == null || c.Errors.Count != 0 || c.CompilerOwnsHandlerError || c.Pipelines.Count != 1 ||
                c.Facts.Count != 1 || c.Facts[0].Key != "IncompleteExecutionContract" || catalog.Compositions.Count(x => x.Identity == c.Identity || x.Key == c.Key || x.GeneratedNamespace == c.GeneratedNamespace) != 1)
                continue;
            var route = c.Pipelines[0];
            var graph = execution.Compositions[i].SingleOrDefault(g => g.Contract.Anchor == route.Anchor);
            if (route.RequestShape is BoundRequestShape.OpenSyncReference or BoundRequestShape.OpenAsyncReference or BoundRequestShape.OpenAllSyncReference or BoundRequestShape.OpenAllAsyncReference)
                continue; // Only explicit generic Send proofs emit these signatures.
            if (graph?.IsValid != true || graph.Schedule == null || route.Family is not (BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync) ||
                route.ResultKind is not (FusionResultKind.Response or FusionResultKind.Void) || route.Handlers.Count != 1 || !route.ExplicitHandlerOrder || route.Stages.Count == 0)
                continue;
            var sync = route.Family == BoundDispatchFamily.SendSync;
            var h = route.Handlers[0];
            if (h.CompilerOwnsError || !c.Handlers.Any(x => x.Identity == h.Type) || sync && h.SyncEmission == null || !sync && h.AsyncEmission == null || h.Sessions.Count > 1)
                continue;
            if (route.Stages.Any(s => s.Kind != FusionStageKind.Hook || s.CompilerOwnsError || s.Capabilities == null || s.Sessions.Count > 1))
                continue;
            var hooks = new List<HookCapabilityEmission>();
            var invalid = false;
            foreach (var s in route.Stages)
            {
                if (s.Sessions.Count == 0)
                {
                    if (s.ServiceOwnership != FusionOwnership.BorrowedService)
                    {
                        invalid = true;
                        break;
                    }

                    hooks.Add(s.Capabilities!);
                }
                else
                {
                    var f = Factory(s.Sessions[0], sync);
                    if (f == null || s.ServiceOwnership != s.Sessions[0].Ownership)
                    {
                        invalid = true;
                        break;
                    }

                    hooks.Add(s.Capabilities! with
                    {
                        Factory = f
                    });
                }
            }
            var hf = h.Sessions.Count == 0 ? null : Factory(h.Sessions[0], sync);
            if (invalid || h.Sessions.Count != 0 && hf == null || h.Sessions.Count == 0 && h.ServiceOwnership != FusionOwnership.BorrowedService)
                continue;
            var handler = sync ? h.SyncEmission! : new BoundSyncHandlerEmission(h.AsyncEmission!.RequestName, h.AsyncEmission.HandlerName, h.AsyncEmission.ContractName, h.AsyncEmission.MethodName, h.AsyncEmission.ResultName) { RequestIsValueType = h.AsyncEmission.RequestIsValueType, HandlerIsValueType = h.AsyncEmission.HandlerIsValueType };
            var registration = new CanonicalSyncCompositionEmission(i, c.GeneratedNamespace, c.MarkerName, c.UseLocalHandlers, handler,
                hf == null ? null : new(hf.FactoryName, hf.ContractName, hf.MethodName, hf.CleanupContractName)
                {
                    FactoryIsValueType = hf.FactoryIsValueType
                }, false, route.PipelineFamily, c.MicrosoftDiProviderName ?? "")
            {
                ExplicitHandlerTypes = c.ExplicitHandlerTypes,
                CatalogMarkerTypes = c.CatalogMarkerTypes,
                ExternAliases = c.ExternAliases,
                RegistrationStages = new(hooks.Select(s => new RegistrationStageEmission(s.HookName, "Hook", s.Factory?.FactoryName))),
                RouteDependencyTypes = !sync && hf == null && !handler.HandlerIsValueType && hooks.Count != 0 &&
                    hooks.All(s => !s.HookIsValueType && s.Factory == null) &&
                    !hooks.All(s => s.Before is { Completion: BoundCompletionKind.Synchronous } && s.Success == null && s.Exception == null && s.Finally == null)
                    ? new(hooks.Select(s => s.HookName).Append(handler.HandlerName).Distinct(System.StringComparer.Ordinal)) : default
            };
            output.Add(new(registration, route.Family.Value, new(hooks), hf)
            {
                HandlerCallTarget = sync ? null : h.AsyncEmission!.CallTarget
            });
        }
        return new(new(output));
    }
    internal static BoundAsyncFactoryEmission? Factory(BoundSessionModel s, bool sync)
    {
        if (!s.SuppliesCallbackReceiver || s.Ownership is not (FusionOwnership.OwnedSession or FusionOwnership.BorrowedResource))
            return null;
        var f = sync ? (s.SyncEmission is { } sf ? new BoundAsyncFactoryEmission(sf.FactoryName, sf.ContractName, sf.MethodName, sf.CleanupContractName, false) { IsSourceDeclaration = sf.IsSourceDeclaration, FactoryIsValueType = sf.FactoryIsValueType } : null) : s.AsyncEmission;
        if (f is null || sync && s.Acquire.Completion != BoundCompletionKind.Synchronous || !sync && s.Acquire.Completion is not (BoundCompletionKind.Synchronous or BoundCompletionKind.ValueTask) ||
            s.Ownership == FusionOwnership.OwnedSession && (f.CleanupContractName == null || s.Cleanup?.Completion != (sync ? BoundCompletionKind.Synchronous : BoundCompletionKind.ValueTask)) || s.Ownership == FusionOwnership.BorrowedResource && (f.CleanupContractName != null || s.Cleanup != null))
            return null;
        return f;
    }
    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalCapabilityEmissionModel model,
        EquatableArray<CanonicalSyncCallSite?> current, CancellationToken token) =>
        CanonicalSyncEmissionNormalizer.BindCallSites(model.Compositions.Select(c => c.Registration.Index), current, token);
}
