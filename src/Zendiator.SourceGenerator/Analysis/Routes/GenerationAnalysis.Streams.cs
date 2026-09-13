using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private readonly List<Route> streamRoutes = new List<Route>();

    private void BuildStreams()
    {
        foreach (var pair in streamRequests.OrderBy(p => Name(p.Key), StringComparer.Ordinal))
        {
            streamHandlers.TryGetValue(pair.Key, out var slist);
            var count = slist?.Count ?? 0;
            if (count == 0)
                Error(1, $"No handler found for {Name(pair.Key)}. Include its assembly.", pair.Key);
            else if (count != 1)
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", slist!.Select(Name))}.", pair.Key);
            else streamRoutes.Add(new Route(pair.Key, pair.Value, slist![0]));
        }
        foreach (var def in openStreamRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            var template = openStreamRequests[def];
            var binds = openStreamHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            if (binds.Count > 1)
            {
                Error(10, $"Ambiguous generic handlers for {Name(def)}: a single stream request supports one open handler.", def);
            }
            else if (binds.Count == 1)
            {
                BuildOpenStreamRoute(def, template, binds[0], streamRoutes);
            }
            else
            {
                var hasClosed = streamHandlers.Keys
                    .Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (!hasClosed)
                    Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
            }
        }
        foreach (var route in streamRoutes)
        {
            if (route.IsOpen || !route.Request.IsGenericType) continue;
            foreach (var binding in openStreamHandlers)
            {
                if (Covers(binding, route.Request))
                    Error(10, $"Ambiguous generic binding for {Name(route.Request)}: closed handler {Name(route.Handler)} overlaps open handler {Name(binding.Definition)}. Keep one.", route.Request);
            }
        }
    }
}
