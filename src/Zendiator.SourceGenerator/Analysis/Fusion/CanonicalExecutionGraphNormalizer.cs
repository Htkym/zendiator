using System.Linq;

namespace Zendiator.SourceGenerator;

internal static class CanonicalExecutionGraphNormalizer
{
    internal static CanonicalExecutionGraphCatalog Create(CompositionSelectionCatalog catalog) => new(new(catalog.Compositions.Select(composition =>
        new EquatableArray<CanonicalExecutionGraphModel>(composition.Pipelines
            // Only fully bound frontend routes reach graph acceptance. Other errors keep their original owner.
            .Where(route => composition.Facts.Any(f => f.Key == "IncompleteExecutionContract" && f.Anchor == route.Anchor))
            .Select(Create)))));

    internal static CanonicalExecutionGraphModel Create(BoundPipelineModel route)
    {
        if (route.RequestShape == BoundRequestShape.ClosedRefLike || route.Stages.Any(s => s.Callbacks.Any(c => CanonicalScopedSyncNormalizer.IsObservation(c.Phase))))
            return CanonicalScopedSyncNormalizer.Graph(route);
        if (route.Family == BoundDispatchFamily.Stream)
        {
            return new(route, null, CanonicalStreamGraphValidator.CheckContract(route))
            {
                NativeStream = true
            };
        }
        var facts = CanonicalSendGraphValidator.CheckContract(route);
        if (facts.Count != 0)
            return new(route, null, facts);
        var schedule = CanonicalSendScheduleBuilder.Build(route);
        return new(route, schedule, CanonicalSendGraphValidator.Validate(route, schedule));
    }
}
