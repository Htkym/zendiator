using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal static class CanonicalAsyncCallBinding
{
    internal static BoundAsyncCallTargetEmission? BindStreamSource(INamedTypeSymbol receiver, IMethodSymbol contract,
        IMethodSymbol implementation, Compilation compilation, Func<ITypeSymbol, string> name) =>
        CanonicalStreamContractBinding.Matches(contract.ContainingType, implementation, compilation)
            ? Bind(receiver, contract, implementation, compilation, name) : null;

    internal static BoundAsyncCallTargetEmission? Bind(INamedTypeSymbol receiver, IMethodSymbol contract,
        IMethodSymbol implementation, Compilation compilation, Func<ITypeSymbol, string> name)
    {
        if (receiver.TypeKind != TypeKind.Class || implementation.ContainingType.TypeKind != TypeKind.Class ||
            implementation.MethodKind != MethodKind.Ordinary || implementation.DeclaredAccessibility != Accessibility.Public ||
            implementation.IsStatic || implementation.Arity != 0 || implementation.ReturnsByRef || implementation.ReturnsByRefReadonly ||
            implementation.HasUnsupportedMetadata || Restricted(implementation) || RestrictedType(implementation.ContainingType) ||
            !implementation.ExplicitInterfaceImplementations.IsEmpty ||
            !compilation.IsSymbolAccessibleWithin(implementation.ContainingType, compilation.Assembly) ||
            !compilation.IsSymbolAccessibleWithin(implementation, compilation.Assembly) ||
            !SymbolEqualityComparer.Default.Equals(implementation.ReturnType, contract.ReturnType) ||
            !Signature(implementation, contract)) return null;
        var members = implementation.ContainingType.GetMembers(implementation.Name).OfType<IMethodSymbol>().ToArray();
        if (members.Any(m => m.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() ==
            "System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute"))) return null;
        var matches = members
            .Where(m => !m.IsStatic && m.Arity == 0 && Signature(m, contract)).ToArray();
        if (matches.Length != 1 || !SymbolEqualityComparer.Default.Equals(matches[0], implementation)) return null;
        return new(name(receiver), name(implementation.ContainingType), implementation.Name,
            new(implementation.Parameters.Select(p => name(p.Type))), !receiver.IsSealed);
    }

    private static bool RestrictedType(INamedTypeSymbol? type) => type != null &&
        (Restricted(type) || RestrictedType(type.ContainingType));

    private static bool Restricted(ISymbol symbol) => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is
        "System.ObsoleteAttribute" or "System.Diagnostics.CodeAnalysis.ExperimentalAttribute" or
        "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute" or "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute" or
        "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute");

    private static bool Signature(IMethodSymbol method, IMethodSymbol contract) =>
        method.Parameters.Length == contract.Parameters.Length && method.Parameters.Zip(contract.Parameters, (left, right) =>
            left.RefKind == RefKind.None && right.RefKind == RefKind.None &&
            SymbolEqualityComparer.Default.Equals(left.Type, right.Type)).All(equal => equal);
}
