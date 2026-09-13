using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private readonly List<Route> routes = new List<Route>();

    private bool IsResponseMulti(ITypeSymbol type) => type.AllInterfaces.Any(i => Same(i.OriginalDefinition, multiResponseDefinition));

    private bool IsVoidMulti(ITypeSymbol type) => type.AllInterfaces.Any(i => Same(i.OriginalDefinition, multiVoidDefinition));

    private bool IsSyncResponseMulti(ITypeSymbol type) => type.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncMultiResponseDefinition));

    private bool IsSyncVoidMulti(ITypeSymbol type) => type.AllInterfaces.Any(i => Same(i.OriginalDefinition, syncMultiVoidDefinition));

    private void BuildRequests()
    {
        foreach (var pair in requests.OrderBy(p => Name(p.Key), StringComparer.Ordinal))
        {
            var multiResp = IsResponseMulti(pair.Key);
            var multiVoid = IsVoidMulti(pair.Key);
            if (multiResp && multiVoid)
            {
                Error(8, $"Conflicting request kinds for {Name(pair.Key)}: response and void multi contracts cannot be combined. Keep one.", pair.Key);
                continue;
            }
            if (multiResp || multiVoid) continue;
            handlers.TryGetValue(pair.Key, out var list);
            voidHandlers.TryGetValue(pair.Key, out var vlist);
            var legacyCount = list?.Count ?? 0;
            var voidCount = vlist?.Count ?? 0;
            if (voidCount != 0 && legacyCount != 0)
            {
                Error(8, $"Conflicting void handlers for {Name(pair.Key)}: native-void and legacy Unit handlers cannot be combined. Keep one contract.", pair.Key);
            }
            else if (voidCount > 1)
            {
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", vlist!.Select(Name))}.", pair.Key);
            }
            else if (voidCount == 1)
            {
                routes.Add(new Route(pair.Key, pair.Value, vlist![0], isVoid: true));
            }
            else if (list == null || list.Count == 0)
                Error(1, $"No handler found for {Name(pair.Key)}. Include its assembly.", pair.Key);
            else if (list.Count != 1)
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", list.Select(Name))}.", pair.Key);
            else routes.Add(new Route(pair.Key, pair.Value, list[0]));
        }
        foreach (var def in openRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            if (IsResponseMulti(def) && IsVoidMulti(def))
            {
                Error(8, $"Conflicting request kinds for {Name(def)}: response and void multi contracts cannot be combined. Keep one.", def);
                continue;
            }
            if (IsResponseMulti(def) || IsVoidMulti(def)) continue;
            var template = openRequests[def];
            var binds = openHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            var vbinds = openVoidHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            if (binds.Count != 0 && vbinds.Count != 0)
            {
                Error(8, $"Conflicting generic handlers for {Name(def)}: native-void and response handlers cannot be combined. Keep one contract.", def);
            }
            else if (binds.Count > 1 || vbinds.Count > 1)
            {
                Error(10, $"Ambiguous generic handlers for {Name(def)}: a single request supports one open handler. Use IMultiRequest for fan-out.", def);
            }
            else if (binds.Count == 1)
            {
                BuildOpenRoute(def, template, binds[0], isVoid: false, isSync: false, routes);
            }
            else if (vbinds.Count == 1)
            {
                BuildOpenRoute(def, template, vbinds[0], isVoid: true, isSync: false, routes);
            }
            else
            {
                var hasClosed = handlers.Keys.Concat(voidHandlers.Keys)
                    .Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (!hasClosed)
                    Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
            }
        }
        foreach (var route in routes)
        {
            if (route.IsOpen || !route.Request.IsGenericType) continue;
            var isRouteVoid = route.IsVoid;
            foreach (var binding in openHandlers.Concat(openVoidHandlers))
            {
                if (binding.IsVoid != isRouteVoid) continue;
                if (Same(binding.RequestDefinition, route.Request.OriginalDefinition) &&
                    (IsResponseMulti(binding.RequestDefinition) || IsVoidMulti(binding.RequestDefinition))) continue;
                if (Covers(binding, route.Request))
                    Error(10, $"Ambiguous generic binding for {Name(route.Request)}: closed handler {Name(route.Handler)} overlaps open handler {Name(binding.Definition)}. Keep one.", route.Request);
            }
        }
    }
}
