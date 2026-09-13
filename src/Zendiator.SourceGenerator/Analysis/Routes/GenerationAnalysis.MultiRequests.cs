using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GenericConstraints;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private readonly List<MultiRoute> multiRoutes = new List<MultiRoute>();

    private void AddMultiBranch(MultiRoute route, INamedTypeSymbol handler, bool handlerIsOpen, string handlerDisplay, string handlerContractDisplay)
    {
        var branch = new MultiBranch
        {
            Handler = handler,
            HandlerIsOpen = handlerIsOpen,
            HandlerDisplay = handlerDisplay,
            HandlerContractDisplay = handlerContractDisplay,
            Order = SubscriberOrder(handler),
            AssemblyId = handler.ContainingAssembly.Identity.ToString(),
            FullName = Name(handler),
        };
        route.Branches.Add(branch);
    }

    private void BuildClosedMulti(INamedTypeSymbol request, ITypeSymbol response, bool isVoid)
    {
        var route = new MultiRoute(request, response, isVoid, isOpen: false);
        var contractName = isVoid
            ? $"global::Zendiator.IRequestHandler<{Name(request)}>"
            : $"global::Zendiator.IRequestHandler<{Name(request)}, {Name(response)}>";
        if (isVoid)
        {
            if (handlers.TryGetValue(request, out var legacy) && legacy.Count != 0)
            {
                Error(8, $"Multi request {Name(request)} requires one-argument handlers. Migrate legacy Unit handlers to IRequestHandler<{Name(request)}>.", request);
                return;
            }
            if (!voidHandlers.TryGetValue(request, out var vlist) || vlist.Count == 0)
            {
                Error(1, $"No handler found for {Name(request)}. Include its assembly.", request);
                return;
            }
            foreach (var handler in vlist)
                AddMultiBranch(route, handler, handlerIsOpen: false, Name(handler), contractName);
        }
        else
        {
            if (voidHandlers.TryGetValue(request, out var vlist) && vlist.Count != 0)
            {
                Error(8, $"Multi request {Name(request)} requires two-argument handlers. Use IRequestHandler<{Name(request)}, {Name(response)}>.", request);
                return;
            }
            if (!handlers.TryGetValue(request, out var list) || list.Count == 0)
            {
                Error(1, $"No handler found for {Name(request)}. Include its assembly.", request);
                return;
            }
            foreach (var handler in list)
                AddMultiBranch(route, handler, handlerIsOpen: false, Name(handler), contractName);
        }
        // Union with covering open bindings: every applicable handler runs.
        var pool = isVoid ? openVoidHandlers : openHandlers;
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
                ? $"global::Zendiator.IRequestHandler<{reqDisplay}>"
                : $"global::Zendiator.IRequestHandler<{reqDisplay}, {respDisplay}>";
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
        multiRoutes.Add(route);
    }

    private void BuildOpenMulti(INamedTypeSymbol def, ITypeSymbol template, List<OpenBinding> binds, bool isVoid)
    {
        var route = new MultiRoute(def, template, isVoid, isOpen: true);
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
                    Error(11, $"Subscriber {Name(binding.Definition)} declares constraints that notification {Name(def)} does not satisfy.", binding.Definition);
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
                ? $"global::Zendiator.IRequestHandler<{reqDisplay}>"
                : $"global::Zendiator.IRequestHandler<{reqDisplay}, {respDisplay}>";
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
        multiRoutes.Add(route);
    }

    private void BuildMultiRequests()
    {
        foreach (var pair in requests.OrderBy(p => Name(p.Key), StringComparer.Ordinal))
        {
            if (IsResponseMulti(pair.Key)) BuildClosedMulti(pair.Key, pair.Value, isVoid: false);
            else if (IsVoidMulti(pair.Key)) BuildClosedMulti(pair.Key, pair.Value, isVoid: true);
        }
        foreach (var def in openRequests.Keys.OrderBy(Name, StringComparer.Ordinal))
        {
            var isResp = IsResponseMulti(def);
            var isVoid = IsVoidMulti(def);
            if (!isResp && !isVoid) continue;
            var template = openRequests[def];
            if (isResp)
            {
                var binds = openHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
                var hasClosed = handlers.Keys.Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (binds.Count == 0)
                {
                    if (!hasClosed) Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
                }
                else BuildOpenMulti(def, template, binds, isVoid: false);
            }
            else
            {
                var binds = openVoidHandlers.Where(b => Same(b.RequestDefinition, def)).ToList();
                var hasClosed = voidHandlers.Keys.Any(k => k.IsGenericType && Same(k.OriginalDefinition, def));
                if (binds.Count == 0)
                {
                    if (!hasClosed) Error(1, $"No handler found for {Name(def)}. Include its assembly.", def);
                }
                else BuildOpenMulti(def, template, binds, isVoid: true);
            }
        }
    }
}
