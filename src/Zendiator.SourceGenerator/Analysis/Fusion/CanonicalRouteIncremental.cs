using System;
using System.Collections.Generic;
using System.Linq;

namespace Zendiator.SourceGenerator;

internal sealed record CanonicalRouteInput(string Key, CompositionSelectionModel Composition, BoundPipelineModel Route);
internal sealed record CanonicalRouteGraph(CanonicalRouteInput Input, CanonicalExecutionGraphModel? Graph);
internal sealed record CanonicalRouteBackend(CanonicalRouteGraph Route, CanonicalSyncCompositionEmission? Sync,
    CanonicalAsyncCompositionEmission? Async, CanonicalWrapperCompositionEmission? Wrapper, CanonicalCapabilityCompositionEmission? Capability)
{
    public CanonicalSendAllSyncCompositionEmission? SendAllSync { get; init; }
    public CanonicalSendAllAsyncCompositionEmission? SendAllAsync { get; init; }
    public CanonicalStreamFallbackModel? StreamCanonical { get; init; }
}
internal sealed record CanonicalRouteText(string Key, string Namespace, string RequestName, BoundDispatchFamily? Family, string Source)
{
    public string StableKey { get; init; } = "";
    public EquatableArray<string> BoxedDependencies { get; init; }
    public bool LazyClosedServiceCache { get; init; }
    // Candidate text must never enter the public registration/partial-route emitter.
    public CanonicalSourceFile? StreamCanonicalFile { get; init; }
}

// Semantic binding refreshes current symbols/locations. Structural values stop unrelated graph/template work.
internal static class CanonicalRouteIncremental
{
    private static string Key(CompositionSelectionModel c, BoundPipelineModel r) => c.Identity + "|" + r.Family + "|" + r.Request + "|" + r.Anchor;
    internal static IEnumerable<CanonicalRouteInput> Project(CompositionSelectionModel c)
    {
        foreach (var r in c.Pipelines)
        {
            var handlerAnchor = "list:" + r.HandlerListOrdinal;
            var relevant = c.Facts.Where(f => f.Anchor == r.Anchor || f.Anchor.StartsWith(r.Anchor + "/", StringComparison.Ordinal) ||
                f.Anchor == handlerAnchor || f.Anchor.StartsWith(handlerAnchor + "/", StringComparison.Ordinal) ||
                f.AdditionalAnchor == r.Anchor || (f.AdditionalAnchor?.StartsWith(r.Anchor + "/", StringComparison.Ordinal) ?? false) ||
                f.AdditionalAnchor == handlerAnchor || (f.AdditionalAnchor?.StartsWith(handlerAnchor + "/", StringComparison.Ordinal) ?? false) ||
                f.Anchor.StartsWith("catalog:", StringComparison.Ordinal));
            var members = new HashSet<string>(r.Handlers.Select(h => h.Type), StringComparer.Ordinal);
            var projected = c with
            {
                Pipelines = new(new[] { r }),
                Facts = new(relevant),
                Handlers = new(c.Handlers.Where(h => members.Contains(h.Identity)))
            };
            yield return new(Key(c, r), projected, r);
        }
    }
    internal static CanonicalRouteGraph Graph(CanonicalRouteInput input) => new(input,
        input.Composition.Facts.Any(f => f.Key == "IncompleteExecutionContract" && f.Anchor == input.Route.Anchor)
            ? CanonicalExecutionGraphNormalizer.Create(input.Route) : null);

    internal static CanonicalExecutionGraphCatalog AssembleGraphs(CompositionSelectionCatalog catalog, IEnumerable<CanonicalRouteGraph> rows)
    {
        var values = rows.GroupBy(r => r.Input.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Graph, StringComparer.Ordinal);
        return new(new(catalog.Compositions.Select(c => new EquatableArray<CanonicalExecutionGraphModel>(c.Pipelines
            .Where(r => c.Facts.Any(f => f.Key == "IncompleteExecutionContract" && f.Anchor == r.Anchor))
            .Select(r => values[Key(c, r)]!)))));
    }
    internal static CanonicalRouteBackend Lower(CanonicalRouteGraph row)
    {
        if (row.Graph == null) return new(row, null, null, null, null);
        var catalog = new CompositionSelectionCatalog(new(new[] { row.Input.Composition }));
        var graphs = new CanonicalExecutionGraphCatalog(new(new[] { new EquatableArray<CanonicalExecutionGraphModel>(new[] { row.Graph! }) }));
        var sync = CanonicalSyncEmissionNormalizer.Create(catalog, graphs);
        var async = CanonicalAsyncEmissionNormalizer.Create(catalog, graphs);
        var wrapper = CanonicalWrapperEmissionNormalizer.Create(catalog, graphs);
        var capability = CanonicalCapabilityEmissionNormalizer.Create(catalog, graphs, sync, async, wrapper);
        return new(row, sync.Compositions.SingleOrDefault(), async.Compositions.SingleOrDefault(),
            wrapper.Compositions.SingleOrDefault(), capability.Compositions.SingleOrDefault())
        {
            SendAllSync = CanonicalSendAllSyncEmissionNormalizer.Create(catalog, graphs).Compositions.SingleOrDefault(),
            SendAllAsync = CanonicalSendAllAsyncEmissionNormalizer.Create(catalog, graphs).Compositions.SingleOrDefault(),
            StreamCanonical = CanonicalStreamFallbackProjection.Create(row)
        };
    }
    internal static CanonicalRouteText Render(CanonicalRouteBackend row)
    {
        var source = row.Sync != null ? CanonicalSyncSourceEmitter.Emit(new(new(new[] { row.Sync }))) :
            row.Async != null ? CanonicalAsyncSourceEmitter.Emit(new(new(new[] { row.Async }))) :
            row.Wrapper != null ? CanonicalWrapperSourceEmitter.Emit(new(new(new[] { row.Wrapper }))) :
            row.Capability != null ? CanonicalCapabilitySourceEmitter.Emit(new(new(new[] { row.Capability }))) :
            row.SendAllSync != null ? CanonicalSendAllSyncSourceEmitter.Emit(row.SendAllSync) :
            row.SendAllAsync != null ? CanonicalSendAllAsyncSourceEmitter.Emit(row.SendAllAsync) : "";
        return new(row.Route.Input.Key, row.Route.Input.Composition.GeneratedNamespace ?? "", row.Route.Input.Route.RequestName,
            row.Route.Input.Route.Family, source)
        {
            StableKey = row.Route.Input.Composition.Identity + "|" + row.Route.Input.Route.Family + "|" + row.Route.Input.Route.Request,
            LazyClosedServiceCache = row.Route.Input.Composition.LazyClosedServiceCache,
            StreamCanonicalFile = row.StreamCanonical is { } canonical ? CanonicalStreamFallbackEmitter.Emit(canonical) : null
        };
    }
}
