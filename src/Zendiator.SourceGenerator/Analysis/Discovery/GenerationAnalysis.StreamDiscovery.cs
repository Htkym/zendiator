using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GenericConstraints;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private bool EnsureOpenStreamRequest(INamedTypeSymbol reqDef)
    {
        if (openStreamRequests.ContainsKey(reqDef)) return true;
        if (!IsOpenDefinition(reqDef) || reqDef.IsRefLikeType)
        {
            Error(3, $"Stream request {Name(reqDef)} must be a public generic definition with exactly one item contract.", reqDef);
            return false;
        }
        var rc = reqDef.AllInterfaces.Where(i => Same(i.OriginalDefinition, streamRequestDefinition)).ToArray();
        if (rc.Length != 1 || !IsPublicDefinition(reqDef) || !LeavesPublicOrParam(rc[0].TypeArguments[0], reqDef))
        {
            Error(3, $"Stream request {Name(reqDef)} must be a public generic definition with exactly one item contract over public or type-parameter types.", reqDef);
            return false;
        }
        if (reqDef.TypeParameters.Any(static p => p.AllowsRefLikeType))
        {
            Error(12, $"Stream request {Name(reqDef)} has ref-like type parameters. Async streams cannot accept ref-like arguments.", reqDef);
            return false;
        }
        if (MayBeRefLike(rc[0].TypeArguments[0], reqDef))
        {
            Error(12, $"Stream request {Name(reqDef)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", reqDef);
            return false;
        }
        openStreamRequests[reqDef] = rc[0].TypeArguments[0];
        return true;
    }

    private void CollectOpenStreamHandler(INamedTypeSymbol type, INamedTypeSymbol contract)
    {
        if (type.TypeKind != TypeKind.Class || HasGenericContainer(type) ||
            !compilation.IsSymbolAccessibleWithin(type, accessContext))
        {
            Error(3, $"Stream handler {Name(type)} must be an accessible class without a generic container.", type);
            return;
        }
        if (contract.TypeArguments[0] is not INamedTypeSymbol pattern || pattern.IsAbstract || pattern.TypeKind == TypeKind.Interface)
        {
            Error(3, $"Stream handler {Name(type)} must target a concrete request pattern.", type);
            return;
        }
        if (!pattern.IsGenericType)
        {
            Error(9, $"Stream handler {Name(type)} has type parameters that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
            return;
        }
        var reqDef = pattern.OriginalDefinition;
        var pargs = pattern.TypeArguments;
        var pmap = new int[pargs.Length];
        var pfixed = new ITypeSymbol[pargs.Length];
        var h2d = new int[type.TypeParameters.Length];
        var hpos = new int[type.TypeParameters.Length];
        for (var j = 0; j < h2d.Length; j++) h2d[j] = -1;
        for (var j = 0; j < hpos.Length; j++) hpos[j] = -1;
        for (var i = 0; i < pargs.Length; i++)
        {
            if (pargs[i] is ITypeParameterSymbol hp && Same(hp.ContainingSymbol, type))
            {
                if (hpos[hp.Ordinal] != -1)
                {
                    Error(9, $"Stream handler {Name(type)} maps {hp.Name} to multiple request type arguments. Give each handler type parameter one position.", type);
                    return;
                }
                pmap[i] = i;
                h2d[hp.Ordinal] = i;
                hpos[hp.Ordinal] = i;
            }
            else if (!HasOpenArguments(pargs[i]) && Public(pargs[i]))
            {
                pmap[i] = -1;
                pfixed[i] = pargs[i];
            }
            else
            {
                Error(9, $"Stream handler {Name(type)} has an unsupported pattern argument {Name(pargs[i])}. Use a handler type parameter or a concrete type.", type);
                return;
            }
        }
        for (var j = 0; j < h2d.Length; j++)
        {
            if (h2d[j] < 0)
            {
                Error(9, $"Stream handler {Name(type)} has type parameter {type.TypeParameters[j].Name} that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
                return;
            }
        }
        if (!EnsureOpenStreamRequest(reqDef)) return;
        var itemPattern = contract.TypeArguments[1];
        if (!LeavesPublicOrParam(itemPattern, type))
        {
            Error(3, $"Stream handler {Name(type)} item {Name(itemPattern)} must be public or built from handler type parameters.", type);
            return;
        }
        if (MayBeRefLike(itemPattern, type))
        {
            Error(12, $"Stream handler {Name(type)} has a ref-like item. Ref struct stream items are not supported; use an ownable item type.", type);
            return;
        }
        var mapped = Substitute(openStreamRequests[reqDef], reqDef, pargs.ToArray(), compilation);
        if (!Same(mapped, itemPattern))
        {
            Error(3, $"Stream handler {Name(type)} item {Name(itemPattern)} does not match request {Name(pattern)} item {Name(mapped)}.", type);
            return;
        }
        if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
        {
            Error(12, $"Stream handler {Name(type)} has ref-like type parameters. Async streams cannot accept ref-like arguments.", type);
            return;
        }
        var merged = reqDef.TypeParameters.Select(FromTypeParam).ToArray();
        var mapArgs = new ITypeSymbol[type.TypeParameters.Length];
        for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = reqDef.TypeParameters[h2d[j]];
        for (var j = 0; j < type.TypeParameters.Length; j++)
        {
            if (!MergeHandlerParam(merged[h2d[j]], type.TypeParameters[j], type, mapArgs, compilation))
            {
                Error(11, $"Stream handler {Name(type)} declares constraints that request {Name(reqDef)} does not satisfy. Loosen the handler or tighten the request.", type);
                return;
            }
        }
        openStreamHandlers.Add(new OpenBinding(type, reqDef, itemPattern, pmap, pfixed, h2d, hpos, isVoid: false) { Merged = merged });
    }
}
