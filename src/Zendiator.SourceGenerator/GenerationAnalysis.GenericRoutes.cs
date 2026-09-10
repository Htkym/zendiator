using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GenericConstraints;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private void BuildOpenRoute(INamedTypeSymbol def, ITypeSymbol template, OpenBinding binding, bool isVoid, bool isSync, List<Route> target)
    {
        var varying = new List<int>();
        for (var i = 0; i < binding.PatternMap.Length; i++)
            if (binding.PatternMap[i] >= 0) varying.Add(i);
        var reqArgs = new string[binding.PatternMap.Length];
        for (var i = 0; i < reqArgs.Length; i++)
            reqArgs[i] = binding.PatternMap[i] >= 0 ? def.TypeParameters[i].Name : Name(binding.PatternFixed[i]);
        var reqDisplay = ClosedGenericName(def, reqArgs);
        string respDisplay = "";
        if (!isVoid)
        {
            var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
            for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
            respDisplay = Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
        }
        var hArgs = new string[binding.Definition.TypeParameters.Length];
        for (var j = 0; j < hArgs.Length; j++) hArgs[j] = def.TypeParameters[binding.HandlerToDef[j]].Name;
        var route = new Route(def, template, binding.Definition, isVoid: isVoid, isSync: isSync) { IsOpen = true };
        foreach (var vi in varying)
        {
            route.OpenTypeParams.Add(def.TypeParameters[vi].Name);
            route.MethodConstraints.Add(RenderConstraints(binding.Merged[vi]));
        }
        route.RequestDisplay = reqDisplay;
        route.ResponseDisplay = respDisplay;
        route.HandlerDisplay = ClosedGenericName(binding.Definition, hArgs);
        route.HandlerContractDisplay = isSync
            ? (isVoid ? $"global::Zendiator.ISyncRequestHandler<{reqDisplay}>" : $"global::Zendiator.ISyncRequestHandler<{reqDisplay}, {respDisplay}>")
            : (isVoid ? $"global::Zendiator.IRequestHandler<{reqDisplay}>" : $"global::Zendiator.IRequestHandler<{reqDisplay}, {respDisplay}>");
        target.Add(route);
    }

    private bool Covers(OpenBinding binding, INamedTypeSymbol closed)
    {
        if (!Same(closed.OriginalDefinition, binding.RequestDefinition)) return false;
        var cargs = closed.TypeArguments;
        if (cargs.Length != binding.PatternMap.Length) return false;
        for (var i = 0; i < binding.PatternMap.Length; i++)
        {
            if (binding.PatternMap[i] < 0 && !Same(binding.PatternFixed[i], cargs[i])) return false;
        }
        var hargs = new ITypeSymbol[binding.HandlerToDef.Length];
        for (var j = 0; j < hargs.Length; j++) hargs[j] = cargs[binding.HandlerPosition[j]];
        return SatisfiesConstraints(binding.Definition, hargs, compilation);
    }

    private void BuildOpenStreamRoute(INamedTypeSymbol def, ITypeSymbol template, OpenBinding binding, List<Route> target)
    {
        var varying = new List<int>();
        for (var i = 0; i < binding.PatternMap.Length; i++)
            if (binding.PatternMap[i] >= 0) varying.Add(i);
        var reqArgs = new string[binding.PatternMap.Length];
        for (var i = 0; i < reqArgs.Length; i++)
            reqArgs[i] = binding.PatternMap[i] >= 0 ? def.TypeParameters[i].Name : Name(binding.PatternFixed[i]);
        var reqDisplay = ClosedGenericName(def, reqArgs);
        var mapArgs = new ITypeSymbol[binding.Definition.TypeParameters.Length];
        for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = def.TypeParameters[binding.HandlerToDef[j]];
        var itemDisplay = Name(Substitute(binding.ResponsePattern, binding.Definition, mapArgs, compilation));
        var hArgs = new string[binding.Definition.TypeParameters.Length];
        for (var j = 0; j < hArgs.Length; j++) hArgs[j] = def.TypeParameters[binding.HandlerToDef[j]].Name;
        var route = new Route(def, template, binding.Definition, isVoid: false, isSync: false) { IsOpen = true };
        foreach (var vi in varying)
        {
            route.OpenTypeParams.Add(def.TypeParameters[vi].Name);
            route.MethodConstraints.Add(RenderConstraints(binding.Merged[vi]));
        }
        route.RequestDisplay = reqDisplay;
        route.ResponseDisplay = itemDisplay;
        route.HandlerDisplay = ClosedGenericName(binding.Definition, hArgs);
        route.HandlerContractDisplay = $"global::Zendiator.IStreamRequestHandler<{reqDisplay}, {itemDisplay}>";
        target.Add(route);
    }
}
