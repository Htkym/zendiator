using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

// The legacy contract still supplies the stage, item boundary and DI service identity.
internal static class CanonicalStructStreamContractBinding
{
    internal static BoundStructStreamCallEmission? Bind(INamedTypeSymbol receiver, INamedTypeSymbol legacy,
        Compilation compilation, out string? error)
    {
        error = null;
        var assembly = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        var definition = assembly?.GetTypeByMetadataName(ContractNames.StructStreamMiddleware);
        var contracts = receiver.AllInterfaces.Where(i => Same(i.OriginalDefinition, definition) &&
            i.TypeArguments.Length == 2 && i.TypeArguments.Zip(legacy.TypeArguments, (left, right) =>
                SymbolEqualityComparer.IncludeNullability.Equals(left, right)).All(equal => equal)).ToArray();
        if (contracts.Length == 0)
            return null;
        var contract = contracts.Length == 1 ? contracts[0] : null;
        var member = contract?.GetMembers().OfType<IMethodSymbol>().SingleOrDefault();
        var method = member == null ? null : receiver.FindImplementationForInterfaceMember(member) as IMethodSymbol;
        if (method == null || !Signature(method, legacy, compilation))
        {
            error = "IStructStreamMiddleware requires OpenAsync<TNext>(request, TNext next, token) returning " +
                "ValueTask<IAsyncEnumerator<TItem>>, with exactly where TNext : struct, IStreamUpstream<TRequest,TItem>.";
            return null;
        }
        // Never introduce a generic virtual/interface dispatch. Unsupported implementations keep
        // the old contract, including a runtime subtype supplied by DI for a nonsealed receiver.
        if (receiver.TypeKind != TypeKind.Class || !receiver.IsSealed || method.MethodKind != MethodKind.Ordinary ||
            method.DeclaredAccessibility != Accessibility.Public || method.IsVirtual || method.IsOverride ||
            method.IsAbstract || !method.ExplicitInterfaceImplementations.IsEmpty ||
            !Same(method.ContainingType, receiver) || method.HasUnsupportedMetadata ||
            !compilation.IsSymbolAccessibleWithin(method, compilation.Assembly) ||
            !compilation.IsSymbolAccessibleWithin(receiver, compilation.Assembly) || Restricted(method) || RestrictedType(receiver))
            return null;
        var members = receiver.GetMembers(method.Name).OfType<IMethodSymbol>().ToArray();
        var generic = members.Where(m => m.Arity == 1).ToArray();
        if (generic.Length != 1 || !Same(generic[0], method) || members.Any(m => m.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute")))
            return null;
        return new(method.Name);
    }

    private static bool Signature(IMethodSymbol method, INamedTypeSymbol legacy, Compilation compilation)
    {
        if (method.IsStatic || method.Arity != 1 || method.Parameters.Length != 3 ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.Parameters.Any(p => p.RefKind != RefKind.None || p.ScopedKind != ScopedKind.None) ||
            !Same(method.ReturnType, legacy.GetMembers().OfType<IMethodSymbol>().Single().ReturnType) ||
            !Same(method.Parameters[0].Type, legacy.TypeArguments[0]) ||
            !Same(method.Parameters[1].Type, method.TypeParameters[0]) ||
            !Same(method.Parameters[2].Type, compilation.GetTypeByMetadataName("System.Threading.CancellationToken")))
            return false;
        var parameter = method.TypeParameters[0];
        var upstream = compilation.GetTypeByMetadataName("Zendiator.IStreamUpstream`2")?.Construct(legacy.TypeArguments.ToArray());
        return parameter.HasValueTypeConstraint && !parameter.HasUnmanagedTypeConstraint && !parameter.AllowsRefLikeType &&
            parameter.ConstraintTypes.Length == 1 && Same(parameter.ConstraintTypes[0], upstream);
    }

    private static bool RestrictedType(INamedTypeSymbol? type) => type != null &&
        (Restricted(type) || RestrictedType(type.ContainingType));

    private static bool Restricted(ISymbol symbol) => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is
        "System.ObsoleteAttribute" or "System.Diagnostics.CodeAnalysis.ExperimentalAttribute" or
        "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute" or "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute" or
        "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute");
}
