using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GenericConstraints;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private bool EnsureOpenRequest(INamedTypeSymbol reqDef)
    {
        if (openRequests.ContainsKey(reqDef)) return true;
        if (!IsOpenDefinition(reqDef) || reqDef.IsRefLikeType)
        {
            Error(3, $"Request {Name(reqDef)} must be a public generic definition with exactly one response contract.", reqDef);
            return false;
        }
        var rc = reqDef.AllInterfaces.Where(i => Same(i.OriginalDefinition, requestDefinition)).ToArray();
        if (rc.Length != 1 || !IsPublicDefinition(reqDef) || !LeavesPublicOrParam(rc[0].TypeArguments[0], reqDef))
        {
            Error(3, $"Request {Name(reqDef)} must be a public generic definition with exactly one response contract over public or type-parameter types.", reqDef);
            return false;
        }
        if (reqDef.TypeParameters.Any(static p => p.AllowsRefLikeType))
        {
            Error(12, $"Request {Name(reqDef)} has ref-like type parameters. Async dispatch cannot accept ref-like arguments; use synchronous dispatch (SendSync).", reqDef);
            return false;
        }
        openRequests[reqDef] = rc[0].TypeArguments[0];
        return true;
    }

    private void CollectOpenHandler(INamedTypeSymbol type, INamedTypeSymbol contract, List<OpenBinding> bucket, bool isVoid)
    {
        if (type.TypeKind != TypeKind.Class || HasGenericContainer(type) ||
            !compilation.IsSymbolAccessibleWithin(type, accessContext))
        {
            Error(3, $"Handler {Name(type)} must be an accessible class without a generic container.", type);
            return;
        }
        if (contract.TypeArguments[0] is not INamedTypeSymbol pattern || pattern.IsAbstract || pattern.TypeKind == TypeKind.Interface)
        {
            Error(3, $"Handler {Name(type)} must target a concrete request pattern.", type);
            return;
        }
        if (!pattern.IsGenericType)
        {
            Error(9, $"Handler {Name(type)} has type parameters that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
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
                    Error(9, $"Handler {Name(type)} maps {hp.Name} to multiple request type arguments. Give each handler type parameter one position.", type);
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
                Error(9, $"Handler {Name(type)} has an unsupported pattern argument {Name(pargs[i])}. Use a handler type parameter or a concrete type.", type);
                return;
            }
        }
        for (var j = 0; j < h2d.Length; j++)
        {
            if (h2d[j] < 0)
            {
                Error(9, $"Handler {Name(type)} has type parameter {type.TypeParameters[j].Name} that cannot be inferred from {Name(pattern)}. Bind every handler type parameter to a request type argument.", type);
                return;
            }
        }
        if (!EnsureOpenRequest(reqDef)) return;
        ITypeSymbol responsePattern = compilation.GetSpecialType(SpecialType.System_Void);
        if (!isVoid)
        {
            responsePattern = contract.TypeArguments[1];
            if (!LeavesPublicOrParam(responsePattern, type))
            {
                Error(3, $"Handler {Name(type)} response {Name(responsePattern)} must be public or built from handler type parameters.", type);
                return;
            }
            var mapped = Substitute(openRequests[reqDef], reqDef, pargs.ToArray(), compilation);
            if (!Same(mapped, responsePattern))
            {
                Error(3, $"Handler {Name(type)} response {Name(responsePattern)} does not match request {Name(pattern)} response {Name(mapped)}.", type);
                return;
            }
        }
        else if (!pattern.AllInterfaces.Any(i => Same(i.OriginalDefinition, voidRequestDefinition)))
        {
            Error(3, $"Handler {Name(type)} request {Name(pattern)} must implement Zendiator.IRequest.", type);
            return;
        }
        if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
        {
            Error(12, $"Handler {Name(type)} has ref-like type parameters. Async dispatch cannot accept ref-like arguments; use synchronous dispatch (SendSync).", type);
            return;
        }
        var merged = reqDef.TypeParameters.Select(FromTypeParam).ToArray();
        var mapArgs = new ITypeSymbol[type.TypeParameters.Length];
        for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = reqDef.TypeParameters[h2d[j]];
        for (var j = 0; j < type.TypeParameters.Length; j++)
        {
            if (!MergeHandlerParam(merged[h2d[j]], type.TypeParameters[j], type, mapArgs, compilation))
            {
                Error(11, $"Handler {Name(type)} declares constraints that request {Name(reqDef)} does not satisfy. Loosen the handler or tighten the request.", type);
                return;
            }
        }
        bucket.Add(new OpenBinding(type, reqDef, responsePattern, pmap, pfixed, h2d, hpos, isVoid) { Merged = merged });
    }
}
