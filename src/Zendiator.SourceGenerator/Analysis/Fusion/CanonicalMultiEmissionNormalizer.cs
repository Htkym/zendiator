using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace Zendiator.SourceGenerator;

internal static class CanonicalMultiEmissionNormalizer
{
    internal static CanonicalMultiEmissionModel Create(CompositionSelectionCatalog catalog, CanonicalExecutionGraphCatalog execution, CanonicalStreamConnectionCatalog? streams = null)
    {
        var result = new List<CanonicalMultiCompositionEmission>();
        for (var i = 0; i < catalog.Compositions.Count; i++)
        {
            var c = catalog.Compositions[i];
            if (c.Pipelines.Count < 2 || c.GeneratedNamespace == null || c.Errors.Count != 0 || c.CompilerOwnsHandlerError ||
                c.Facts.Count != c.Pipelines.Count || c.Facts.Any(f => f.Key != "IncompleteExecutionContract") ||
                catalog.Compositions.Count(x => x.Identity == c.Identity || x.Key == c.Key || x.GeneratedNamespace == c.GeneratedNamespace) != 1) continue;
            if (c.Handlers.Any(h => !c.Pipelines.Any(r => HasHandler(r, h))) ||
                c.Pipelines.Select(r => r.Family + "|" + r.RequestRuntimeIdentity).Distinct(StringComparer.Ordinal).Count() != c.Pipelines.Count ||
                c.Pipelines.Any(r => r.RequestShape == BoundRequestShape.ClosedRefLike ||
                    r.ResultKind == FusionResultKind.Response && r.Handlers.Any(h => !h.ResponseContract))) continue;
            var routes = new List<CanonicalMultiRouteEmission>();
            foreach (var route in c.Pipelines)
            {
                var graph = execution.Compositions[i].SingleOrDefault(g => g.Contract.Anchor == route.Anchor);
                if (graph?.IsValid != true) break;
                if (route.Family == BoundDispatchFamily.Stream)
                {
                    var acceptedStream = streams == null ? null : CanonicalStreamPublicConnection.MixedRoute(catalog, c, route, streams, i);
                    if (acceptedStream == null) break;
                    var streamKey = c.Identity + "|" + route.Family + "|" + route.Request + "|" + route.Result;
                    routes.Add(new(streamKey, route.HandlerListOrdinal, route.PipelineListOrdinal,
                        acceptedStream.Registration, null, null, null, null)
                    { Stream = acceptedStream, StreamStableKey = c.Identity + "|" + route.Family + "|" + route.Request });
                    continue;
                }
                var projected = new CompositionSelectionCatalog(new(new[] { c with {
                    Handlers = new(c.Handlers.Where(h => HasHandler(route, h))),
                    Pipelines = new(new[] { route }), Facts = new(c.Facts.Where(f => f.Anchor == route.Anchor)) } }));
                var graphs = new CanonicalExecutionGraphCatalog(new(new[] { new EquatableArray<CanonicalExecutionGraphModel>(new[] { graph }) }));
                var sync = CanonicalSyncEmissionNormalizer.Create(projected, graphs);
                var async = CanonicalAsyncEmissionNormalizer.Create(projected, graphs);
                var wrapper = CanonicalWrapperEmissionNormalizer.Create(projected, graphs);
                var capability = CanonicalCapabilityEmissionNormalizer.Create(projected, graphs, sync, async, wrapper);
                var sendAll = CanonicalSendAllSyncEmissionNormalizer.Create(projected, graphs).Compositions.SingleOrDefault();
                var allAsync = CanonicalSendAllAsyncEmissionNormalizer.Create(projected, graphs).Compositions.SingleOrDefault();
                var s = sync.Compositions.SingleOrDefault(); var a = async.Compositions.SingleOrDefault(); var w = wrapper.Compositions.SingleOrDefault(); var h = capability.Compositions.SingleOrDefault();
                if ((s == null ? 0 : 1) + (a == null ? 0 : 1) + (w == null ? 0 : 1) + (h == null ? 0 : 1) + (sendAll == null ? 0 : 1) + (allAsync == null ? 0 : 1) != 1) break;
                var registration = (s ?? (a == null ? null : CanonicalAsyncEmissionNormalizer.Registration(a)) ?? w?.Registration ?? h?.Registration ?? sendAll?.Registration ?? allAsync!.Registration) with { Index = i };
                var key = c.Identity + "|" + route.Family + "|" + route.Request + "|" + route.Result;
                routes.Add(new(key, route.HandlerListOrdinal, route.PipelineListOrdinal, registration, s, a, w, h) { SendAllSync = sendAll, SendAllAsync = allAsync });
            }
            var composition = new CanonicalMultiCompositionEmission(i, c.GeneratedNamespace, new(routes));
            if (routes.Count == c.Pipelines.Count && CanonicalRegistrationPlan.Compatible(composition)) result.Add(composition);
        }
        return new(new(result));
    }
    internal static bool HasHandler(BoundPipelineModel route, SelectedHandlerModel handler) => (route.SendOpenRegistration ?? route.StreamOpenRegistration) is { } open
        ? open.HandlerMembership.Contains(handler.Identity) : route.Handlers.Any(h => h.Type == handler.Identity);

    internal static CanonicalMultiEmissionModel Connected(CanonicalMultiEmissionModel model, CanonicalSyncInterceptorModel sites) =>
        new(new(model.Compositions.Where(c =>
            sites.CallSites.Count(s => s.Namespace == c.Namespace && s.MarkerName == c.Routes[0].Registration.MarkerName) == 1)));

    internal static IEnumerable<CanonicalRouteText> StreamRouteTexts(CanonicalMultiEmissionModel model) =>
        model.Compositions.SelectMany(c => c.Routes.Where(r => r.Stream != null).OrderBy(r => r.Key, StringComparer.Ordinal).Select(r =>
            new CanonicalRouteText(r.Key, c.Namespace, r.Registration.Handler.RequestName, BoundDispatchFamily.Stream,
                CanonicalStreamPublicConnection.Source(r.Stream!))
            { StableKey = r.StreamStableKey! }));

    internal static CanonicalSyncInterceptorModel BindCallSites(CanonicalMultiEmissionModel model,
        EquatableArray<CanonicalSyncCallSite?> current, CancellationToken ct) =>
        CanonicalSyncEmissionNormalizer.BindCallSites(model.Compositions.Select(c => c.Index), current, ct);
}
