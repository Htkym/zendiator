using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GenericConstraints;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private readonly List<Route> syncRoutes = new List<Route>();
    private readonly List<MultiRoute> syncMultiRoutes = new List<MultiRoute>();

    private void BuildClosedSyncMulti(INamedTypeSymbol request, ITypeSymbol response, bool isVoid)
    {
        var route = new MultiRoute(request, response, isVoid, isOpen: false, isSync: true);
        var contractName = isVoid
            ? $"global::Zendiator.ISyncRequestHandler<{Name(request)}>"
            : $"global::Zendiator.ISyncRequestHandler<{Name(request)}, {Name(response)}>";
        if (isVoid)
        {
            if (syncHandlers.TryGetValue(request, out var legacy) && legacy.Count != 0)
            {
                Error(8, $"Multi request {Name(request)} requires one-argument sync handlers. Use ISyncRequestHandler<{Name(request)}>.", request);
                return;
            }
            if (!syncVoidHandlers.TryGetValue(request, out var vlist) || vlist.Count == 0)
            {
                Error(1, $"No handler found for {Name(request)}. Include its assembly.", request);
                return;
            }
            foreach (var handler in vlist)
                AddMultiBranch(route, handler, handlerIsOpen: false, Name(handler), contractName);
        }
        else
        {
            if (syncVoidHandlers.TryGetValue(request, out var vlist) && vlist.Count != 0)
            {
                Error(8, $"Multi request {Name(request)} requires two-argument sync handlers. Use ISyncRequestHandler<{Name(request)}, {Name(response)}>.", request);
                return;
            }
            if (!syncHandlers.TryGetValue(request, out var list) || list.Count == 0)
            {
                Error(1, $"No handler found for {Name(request)}. Include its assembly.", request);
                return;
            }
            foreach (var handler in list)
                AddMultiBranch(route, handler, handlerIsOpen: false, Name(handler), contractName);
        }
        var pool = isVoid ? openSyncVoidHandlers : openSyncHandlers;
        foreach (var binding in pool)
        {
            if (!Same(binding.RequestDefinition, request.OriginalDefinition) && !Same(binding.RequestDefinition, request)) continue;
            if (!Covers(binding, request)) continue;
            var hArgs = new string[binding.Definition.TypeParameters.Length];
            for (var j = 0; j < hArgs.Length; j++)
                hArgs[j] = Name(request.TypeArguments[binding.HandlerPosition[j]]);
            var display = ClosedGenericName(binding.Definition, hArgs);
            var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
            for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = request.TypeArguments[binding.HandlerPosition[j]];
            var respDisplay = isVoid ? "" : Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
            var reqDisplay = Name(request);
            var contractDisplay = isVoid
                ? $"global::Zendiator.ISyncRequestHandler<{reqDisplay}>"
                : $"global::Zendiator.ISyncRequestHandler<{reqDisplay}, {respDisplay}>";
            if (route.Branches.Any(b => Same(b.Handler, binding.Definition))) continue;
            AddMultiBranch(route, binding.Definition, handlerIsOpen: true, display, contractDisplay);
        }
        route.Branches.Sort(static (a, b) =>
        {
            var order = a.Order.CompareTo(b.Order);
            if (order != 0) return order;
            var assembly = string.Compare(a.AssemblyId, b.AssemblyId, StringComparison.Ordinal);
            return assembly != 0 ? assembly : string.Compare(a.FullName, b.FullName, StringComparison.Ordinal);
        });
        syncMultiRoutes.Add(route);
    }

    private void BuildOpenSyncMulti(INamedTypeSymbol def, ITypeSymbol template, List<OpenBinding> binds, bool isVoid)
    {
        var route = new MultiRoute(def, template, isVoid, isOpen: true, isSync: true);
        var reqArgs = new string[binds[0].PatternMap.Length];
        var varying = new List<int>();
        for (var i = 0; i < reqArgs.Length; i++)
        {
            if (binds.All(b => b.PatternMap[i] >= 0))
            {
                varying.Add(i);
                reqArgs[i] = def.TypeParameters[i].Name;
            }
            else if (binds.All(b => b.PatternMap[i] < 0 && Same(b.PatternFixed[i], binds[0].PatternFixed[i])))
            {
                reqArgs[i] = Name(binds[0].PatternFixed[i]);
            }
            else
            {
                Error(10, $"Ambiguous generic binding for {Name(def)}: open multi handlers disagree on type argument {i}. Align the patterns.", def);
                return;
            }
        }
        var reqDisplay = ClosedGenericName(def, reqArgs);
        var merged = def.TypeParameters.Select(FromTypeParam).ToArray();
        for (var i = 0; i < merged.Length; i++) merged[i].AllowsRefLike = def.TypeParameters[i].AllowsRefLikeType;
        var ordered = binds
            .Select(b => (Binding: b, Order: SubscriberOrder(b.Definition), Assembly: b.Definition.ContainingAssembly.Identity.ToString(), Full: Name(b.Definition)))
            .OrderBy(static x => x.Order)
            .ThenBy(static x => x.Assembly, StringComparer.Ordinal)
            .ThenBy(static x => x.Full, StringComparer.Ordinal)
            .Select(static x => x.Binding).ToList();
        foreach (var binding in ordered)
        {
            var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
            for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
            for (var j = 0; j < binding.Definition.TypeParameters.Length; j++)
            {
                if (!MergeHandlerParam(merged[binding.HandlerToDef[j]], binding.Definition.TypeParameters[j], binding.Definition, mapArgs, compilation))
                {
                    Error(11, $"Handler {Name(binding.Definition)} declares constraints that request {Name(def)} does not satisfy.", binding.Definition);
                    return;
                }
            }
            var hArgs = new string[binding.Definition.TypeParameters.Length];
            for (var j = 0; j < hArgs.Length; j++)
            {
                var pos = binding.HandlerPosition[j];
                hArgs[j] = binding.PatternMap[pos] >= 0 ? def.TypeParameters[pos].Name : Name(binding.PatternFixed[pos]);
            }
            var display = ClosedGenericName(binding.Definition, hArgs);
            string respDisplay = "";
            if (!isVoid) respDisplay = Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
            var contractDisplay = isVoid
                ? $"global::Zendiator.ISyncRequestHandler<{reqDisplay}>"
                : $"global::Zendiator.ISyncRequestHandler<{reqDisplay}, {respDisplay}>";
            AddMultiBranch(route, binding.Definition, handlerIsOpen: true, display, contractDisplay);
        }
        foreach (var vi in varying)
        {
            route.OpenTypeParams.Add(def.TypeParameters[vi].Name);
            route.MethodConstraints.Add(RenderConstraints(merged[vi]));
        }
        route.RequestDisplay = reqDisplay;
        if (!isVoid)
        {
            var first = binds[0];
            var firstMap = new ITypeSymbol[first.Definition.TypeParameters.Length];
            for (var j = 0; j < firstMap.Length; j++) firstMap[j] = def.TypeParameters[first.HandlerToDef[j]];
            route.ResponseDisplay = Name(Substitute(first.ResponsePattern, first.Definition, firstMap, compilation));
        }
        syncMultiRoutes.Add(route);
    }

    private void BuildSyncRequests()
    {
        foreach (var pair in syncRequests.OrderBy(p => Name(p.Key), StringComparer.Ordinal))
        {
            var mr = IsSyncResponseMulti(pair.Key);
            var mv = IsSyncVoidMulti(pair.Key);
            if (mr && mv)
            {
                Error(8, $"Conflicting request kinds for {Name(pair.Key)}: response and void sync multi contracts cannot be combined. Keep one.", pair.Key);
                continue;
            }
            if (mr)
            {
                BuildClosedSyncMulti(pair.Key, pair.Value, isVoid: false);
                continue;
            }
            if (mv)
            {
                BuildClosedSyncMulti(pair.Key, pair.Value, isVoid: true);
                continue;
            }
            syncHandlers.TryGetValue(pair.Key, out var list);
            syncVoidHandlers.TryGetValue(pair.Key, out var vlist);
            var rc = list?.Count ?? 0;
            var vc = vlist?.Count ?? 0;
            if (rc != 0 && vc != 0)
            {
                Error(8, $"Conflicting sync handlers for {Name(pair.Key)}: response and void handlers cannot be combined. Keep one contract.", pair.Key);
            }
            else if (vc > 1)
            {
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", vlist!.Select(Name))}.", pair.Key);
            }
            else if (vc == 1)
            {
                syncRoutes.Add(new Route(pair.Key, pair.Value, vlist![0], isVoid: true, isSync: true));
            }
            else if (rc == 0)
                Error(1, $"No handler found for {Name(pair.Key)}. Include its assembly.", pair.Key);
            else if (rc > 1)
                Error(2, $"Multiple handlers found for {Name(pair.Key)}: {string.Join(", ", list!.Select(Name))}.", pair.Key);
            else syncRoutes.Add(new Route(pair.Key, pair.Value, list![0], isSync: true));
        }
        foreach (var req in syncVoidRequests.OrderBy(Name, StringComparer.Ordinal))
        {
            if (syncRequests.ContainsKey(req) || IsOpenDefinition(req)) continue;
            var mr = IsSyncResponseMulti(req);
            var mv = IsSyncVoidMulti(req);
            if (mr && mv)
            {
                Error(8, $"Conflicting request kinds for {Name(req)}: response and void sync multi contracts cannot be combined. Keep one.", req);
                continue;
            }
            if (mr)
            {
                Error(8, $"Multi request {Name(req)} must implement ISyncRequest<TResponse>.", req);
                continue;
            }
            if (mv)
            {
                BuildClosedSyncMulti(req, compilation.GetSpecialType(SpecialType.System_Void), isVoid: true);
                continue;
            }
            syncVoidHandlers.TryGetValue(req, out var vlist);
            syncHandlers.TryGetValue(req, out var list);
            var vc = vlist?.Count ?? 0;
            var rc = list?.Count ?? 0;
            if (rc != 0)
            {
                Error(8, $"Handler {Name(list![0])} targets void sync request {Name(req)}. Use one-argument ISyncRequestHandler<{Name(req)}>.", req);
            }
            else if (vc == 0)
                Error(1, $"No handler found for {Name(req)}. Include its assembly.", req);
            else if (vc > 1)
                Error(2, $"Multiple handlers found for {Name(req)}: {string.Join(", ", vlist!.Select(Name))}.", req);
            else syncRoutes.Add(new Route(req, compilation.GetSpecialType(SpecialType.System_Void), vlist![0], isVoid: true, isSync: true));
        }
        foreach (var def in openSyncRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            if (IsSyncResponseMulti(def) && IsSyncVoidMulti(def))
            {
                Error(8, $"Conflicting request kinds for {Name(def)}: response and void sync multi contracts cannot be combined. Keep one.", def);
                continue;
            }
            if (IsSyncResponseMulti(def) || IsSyncVoidMulti(def)) continue;
            var template = openSyncRequests[def];
            var binds = openSyncHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            var vbinds = openSyncVoidHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
            if (binds.Count != 0 && vbinds.Count != 0)
            {
                Error(8, $"Conflicting generic handlers for {Name(def)}: native-void and response handlers cannot be combined. Keep one contract.", def);
            }
            else if (binds.Count > 1 || vbinds.Count > 1)
            {
                Error(10, $"Ambiguous generic handlers for {Name(def)}: a single request supports one open handler. Use ISyncMultiRequest for fan-out.", def);
            }
            else if (binds.Count == 1)
            {
                BuildOpenRoute(def, template, binds[0], isVoid: false, isSync: true, syncRoutes);
            }
            else if (vbinds.Count == 1)
            {
                BuildOpenRoute(def, template, vbinds[0], isVoid: true, isSync: true, syncRoutes);
            }
            else
            {
                var hasClosed = syncHandlers.Keys.Concat(syncVoidHandlers.Keys)
                    .Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (!hasClosed)
                    Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
            }
        }
        foreach (var route in syncRoutes)
        {
            if (route.IsOpen || !route.Request.IsGenericType) continue;
            var isRouteVoid = route.IsVoid;
            foreach (var binding in openSyncHandlers.Concat(openSyncVoidHandlers))
            {
                if (binding.IsVoid != isRouteVoid) continue;
                if (Covers(binding, route.Request))
                    Error(10, $"Ambiguous generic binding for {Name(route.Request)}: closed handler {Name(route.Handler)} overlaps open handler {Name(binding.Definition)}. Keep one.", route.Request);
            }
        }
        foreach (var route in syncRoutes)
        {
            if (route.IsOpen || !route.Request.IsGenericType) continue;
            var isRouteVoid = route.IsVoid;
            foreach (var binding in openSyncHandlers.Concat(openSyncVoidHandlers))
            {
                if (binding.IsVoid != isRouteVoid) continue;
                if (Covers(binding, route.Request))
                    Error(10, $"Ambiguous generic binding for {Name(route.Request)}: closed handler {Name(route.Handler)} overlaps open handler {Name(binding.Definition)}. Keep one.", route.Request);
            }
        }
        foreach (var def in openSyncRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            var isResp = IsSyncResponseMulti(def);
            var isVoid = IsSyncVoidMulti(def);
            if (!isResp && !isVoid) continue;
            var template = openSyncRequests[def];
            if (isResp)
            {
                var binds = openSyncHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
                var hasClosed = syncHandlers.Keys.Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (binds.Count == 0)
                {
                    if (!hasClosed) Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
                }
                else BuildOpenSyncMulti(def, template, binds, isVoid: false);
            }
            else
            {
                var binds = openSyncVoidHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
                var hasClosed = syncVoidHandlers.Keys.Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (binds.Count == 0)
                {
                    if (!hasClosed) Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
                }
                else BuildOpenSyncMulti(def, template, binds, isVoid: true);
            }
        }
    }
}
