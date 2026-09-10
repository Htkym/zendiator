using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GenericConstraints;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private bool EnsureOpenNotification(INamedTypeSymbol reqDef)
    {
        if (openNotifications.Contains(reqDef)) return true;
        if (!IsOpenDefinition(reqDef) || !IsPublicDefinition(reqDef) ||
            !reqDef.AllInterfaces.Any(i => Same(i.OriginalDefinition, notificationDefinition)))
        {
            Error(3, $"Notification {Name(reqDef)} must be a public generic definition implementing Zendiator.INotification.", reqDef);
            return false;
        }
        openNotifications.Add(reqDef);
        return true;
    }

    private void CollectOpenSubscriber(INamedTypeSymbol type, INamedTypeSymbol contract)
    {
        if (type.TypeKind != TypeKind.Class || HasGenericContainer(type) ||
            !compilation.IsSymbolAccessibleWithin(type, accessContext))
        {
            Error(3, $"Subscriber {Name(type)} must be an accessible class without a generic container.", type);
            return;
        }
        if (contract.TypeArguments[0] is not INamedTypeSymbol pattern || pattern.IsAbstract || pattern.TypeKind == TypeKind.Interface)
        {
            Error(3, $"Subscriber {Name(type)} must target a concrete notification pattern.", type);
            return;
        }
        if (!pattern.IsGenericType)
        {
            Error(9, $"Subscriber {Name(type)} has type parameters that cannot be inferred from {Name(pattern)}. Bind every subscriber type parameter to a notification type argument.", type);
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
                    Error(9, $"Subscriber {Name(type)} maps {hp.Name} to multiple notification type arguments. Give each subscriber type parameter one position.", type);
                    return;
                }
                pmap[i] = i;
                h2d[hp.Ordinal] = i;
                hpos[hp.Ordinal] = i;
            }
            else
            {
                Error(9, $"Subscriber {Name(type)} must use a type parameter for every pattern argument of {Name(pattern)}. Fixed open notification patterns are not supported; add a closed subscriber instead.", type);
                return;
            }
        }
        for (var j = 0; j < h2d.Length; j++)
        {
            if (h2d[j] < 0)
            {
                Error(9, $"Subscriber {Name(type)} has type parameter {type.TypeParameters[j].Name} that cannot be inferred from {Name(pattern)}.", type);
                return;
            }
        }
        if (!EnsureOpenNotification(reqDef)) return;
        if (type.TypeParameters.Any(static p => p.AllowsRefLikeType))
        {
            Error(12, $"Subscriber {Name(type)} has ref-like type parameters. Ref struct notifications are not supported.", type);
            return;
        }
        var merged = reqDef.TypeParameters.Select(FromTypeParam).ToArray();
        var mapArgs = new ITypeSymbol[type.TypeParameters.Length];
        for (var j = 0; j < mapArgs.Length; j++) mapArgs[j] = reqDef.TypeParameters[h2d[j]];
        for (var j = 0; j < type.TypeParameters.Length; j++)
        {
            if (!MergeHandlerParam(merged[h2d[j]], type.TypeParameters[j], type, mapArgs, compilation))
            {
                Error(11, $"Subscriber {Name(type)} declares constraints that notification {Name(reqDef)} does not satisfy.", type);
                return;
            }
        }
        openSubscribers.Add(new OpenBinding(type, reqDef, compilation.GetSpecialType(SpecialType.System_Void), pmap, pfixed, h2d, hpos, isVoid: true) { Merged = merged });
    }
}
