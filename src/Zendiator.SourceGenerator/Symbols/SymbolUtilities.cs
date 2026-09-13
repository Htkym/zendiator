using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class SymbolUtilities
{
    internal static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                                   SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                                   SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static string? FullNameOf(ITypeSymbol type)
    {
        // Match Type.ToString(): reference and implementation assemblies can
        // differ for forwarded generic arguments (for example System.Int32).
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FullNameOf(array.ElementType) is string element
                    ? element + "[" + new string(',', array.Rank - 1) + "]"
                    : null;
            case INamedTypeSymbol named when named.IsUnboundGenericType:
                return QualifiedMetadataName(named.OriginalDefinition);
            case INamedTypeSymbol named when named.IsGenericType && !IsOpenDefinition(named):
                {
                    var definition = QualifiedMetadataName(named.OriginalDefinition);
                    if (definition == null) return null;
                    var args = new List<string>();
                    foreach (var argument in named.TypeArguments)
                    {
                        var name = FullNameOf(argument);
                        if (name == null) return null;
                        args.Add(name);
                    }
                    return definition + "[" + string.Join(",", args) + "]";
                }
            case INamedTypeSymbol named:
                return QualifiedMetadataName(named.OriginalDefinition);
            default:
                return null;
        }
    }

    internal static string? QualifiedMetadataName(INamedTypeSymbol definition)
    {
        if (definition.ContainingNamespace == null) return null;
        var chain = new Stack<string>();
        for (var t = definition; t != null; t = t.ContainingType) chain.Push(t.MetadataName);
        var ns = definition.ContainingNamespace.IsGlobalNamespace ? "" : definition.ContainingNamespace.ToDisplayString() + ".";
        return ns + string.Join("+", chain);
    }

    internal static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var member in ns.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is INamespaceSymbol child)
                foreach (var type in Types(child, ct)) yield return type;
            else if (member is INamedTypeSymbol named)
                foreach (var type in Types(named, ct)) yield return type;
        }
    }

    internal static IEnumerable<INamedTypeSymbol> Types(INamedTypeSymbol named, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        yield return named;
        foreach (var nested in named.GetTypeMembers())
            foreach (var type in Types(nested, ct)) yield return type;
    }

    internal static bool HasGenericContainer(INamedTypeSymbol type) => type.ContainingType != null && HasParameters(type.ContainingType);

    internal static bool HasParameters(INamedTypeSymbol type) => type.Arity != 0 || HasGenericContainer(type);

    internal static bool Public(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol a => Public(a.ElementType),
        INamedTypeSymbol n => n.DeclaredAccessibility == Accessibility.Public &&
            (n.ContainingType == null || Public(n.ContainingType)) && n.TypeArguments.All(Public),
        IDynamicTypeSymbol => true,
        _ => false
    };

    internal static bool Same(ISymbol? a, ISymbol? b) => SymbolEqualityComparer.Default.Equals(a, b);

    internal static string Name(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    internal static bool IsOpenDefinition(INamedTypeSymbol type) =>
        type.IsUnboundGenericType ||
        (type.IsGenericType && type.TypeParameters.Length != 0 && Same(type.OriginalDefinition, type));

    internal static bool HasOpenArguments(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol a => HasOpenArguments(a.ElementType),
        INamedTypeSymbol n => n.TypeArguments.Any(HasOpenArguments),
        _ => false
    };

    internal static bool IsPublicDefinition(INamedTypeSymbol type)
    {
        for (var t = type; t != null; t = t.ContainingType)
            if (t.DeclaredAccessibility != Accessibility.Public) return false;
        return true;
    }

    internal static bool LeavesPublicOrParam(ITypeSymbol type, INamedTypeSymbol owner)
    {
        if (type is IDynamicTypeSymbol) return true;
        if (type is ITypeParameterSymbol p) return Same(p.ContainingSymbol, owner);
        if (type is IArrayTypeSymbol a) return LeavesPublicOrParam(a.ElementType, owner);
        if (type is INamedTypeSymbol n)
        {
            if (!IsPublicDefinition(n.OriginalDefinition)) return false;
            return n.TypeArguments.All(t => LeavesPublicOrParam(t, owner));
        }
        return false;
    }

    internal static string QualifiedName(INamedTypeSymbol def)
    {
        var chain = new List<string>();
        for (var t = def; t != null; t = t.ContainingType) chain.Add(t.Name);
        chain.Reverse();
        var ns = def.ContainingNamespace.IsGlobalNamespace ? null : def.ContainingNamespace.ToDisplayString();
        return (ns == null ? "global::" : "global::" + ns + ".") + string.Join(".", chain);
    }

    internal static string ClosedGenericName(INamedTypeSymbol def, string[] ownArgs)
    {
        var name = QualifiedName(def);
        return ownArgs.Length == 0 ? name : name + "<" + string.Join(", ", ownArgs) + ">";
    }

    internal static string OpenTypeofName(INamedTypeSymbol def) =>
        QualifiedName(def) + "<" + new string(',', def.Arity - 1) + ">";

    internal static bool MayBeRefLike(ITypeSymbol type, INamedTypeSymbol owner) => type switch
    {
        INamedTypeSymbol n when n.IsRefLikeType => true,
        ITypeParameterSymbol p => Same(p.ContainingSymbol, owner) && p.AllowsRefLikeType,
        IArrayTypeSymbol a => MayBeRefLike(a.ElementType, owner),
        INamedTypeSymbol n => n.TypeArguments.Any(t => MayBeRefLike(t, owner)),
        _ => false
    };

    // Direct call is safe: virtual dispatch on the concrete receiver reaches the same override.
    internal static bool UseDirectCall(INamedTypeSymbol closed, INamedTypeSymbol ifaceDefinition, string methodName = "HandleAsync")
    {
        var interfaces = closed.AllInterfaces.Where(i => Same(i.OriginalDefinition, ifaceDefinition)).ToArray();
        // With multiple contracts, use the route-specific interface cast. A
        // public overload need not implement the contract for this request.
        if (interfaces.Length != 1) return false;
        var iface = interfaces[0];
        var target = iface?.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        if (target == null) return false;
        if (closed.FindImplementationForInterfaceMember(target) is not IMethodSymbol impl) return false;
        return impl.ExplicitInterfaceImplementations.IsEmpty &&
            impl.ContainingType != null && Same(impl.ContainingType.OriginalDefinition, closed.OriginalDefinition);
    }
}
