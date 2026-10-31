using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

/// <summary>Extracts marked source and explicitly supplied metadata targets. Interfaces verify these targets.</summary>
internal static class MarkedHandlerDiscovery
{
    internal const string MarkerName = "Zendiator.ZendiatorHandlerAttribute";
    // Symbols and locations stay in the current binding stream, outside value equality.
    internal sealed record Candidate(MarkedHandlerModel Model, INamedTypeSymbol Symbol, Location Location);

    internal static Candidate? Bind(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        var compilation = context.SemanticModel.Compilation;
        var handler = compilation.GetTypeByMetadataName(ContractNames.Handler);
        var marker = handler?.ContainingAssembly.GetTypeByMetadataName(MarkerName);
        if (marker == null || context.TargetSymbol is not INamedTypeSymbol type)
            return null;
        return BindType(compilation, type, marker, true, ct);
    }

    // Known external types are supplied by explicit configuration, never by assembly-wide scanning.
    internal static Candidate? BindExplicitMetadata(Compilation compilation, INamedTypeSymbol type, CancellationToken ct)
    {
        if (type.Locations.Any(l => l.IsInSource) || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
            return null;
        var marker = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly.GetTypeByMetadataName(MarkerName);
        if (marker == null || type.GetAttributes().Count(a => Same(a.AttributeClass, marker)) > 1)
            return null;
        return BindType(compilation, type, marker, false, ct);
    }

    private static Candidate? BindType(Compilation compilation, INamedTypeSymbol type, INamedTypeSymbol marker, bool markerRequired, CancellationToken ct)
    {
        var attributes = type.GetAttributes().Where(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, marker)).ToArray();
        if (markerRequired && attributes.Length == 0)
            return null;
        var contracts = new List<MarkedHandlerContract>();
        foreach (var metadataName in new[] { ContractNames.Handler, ContractNames.VoidHandler,
            ContractNames.SyncHandler, ContractNames.SyncVoidHandler, ContractNames.StreamHandler, ContractNames.EnumerableStreamHandler, ContractNames.NotificationHandler })
        {
            ct.ThrowIfCancellationRequested();
            var definition = compilation.GetTypeByMetadataName(metadataName);
            foreach (var contract in type.AllInterfaces.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, definition)))
            {
                var members = contract.GetMembers().OfType<IMethodSymbol>().ToArray();
                var implementation = members.Length == 1 ? type.FindImplementationForInterfaceMember(members[0]) as IMethodSymbol : null;
                var method = implementation == null ? "<missing>" :
                    implementation.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) +
                    "/" + implementation.DeclaredAccessibility + "/explicit=" + !implementation.ExplicitInterfaceImplementations.IsEmpty +
                    "/virtual=" + implementation.IsVirtual + "/override=" + implementation.IsOverride + "/abstract=" + implementation.IsAbstract +
                    "/return=" + Identity(implementation.ReturnType) + "/constraints=" + Constraints(implementation.TypeParameters);
                contracts.Add(new(metadataName, Identity(contract.TypeArguments[0]),
                    contract.TypeArguments.Length >= 2 ? Identity(contract.TypeArguments[1]) : null, method,
                    Constraints(contract.OriginalDefinition.TypeParameters)));
            }
        }
        var hierarchy = new List<string>();
        for (var current = type; current != null; current = current.BaseType)
        {
            hierarchy.Add(Identity(current) + "/" + current.TypeKind + "/" + current.DeclaredAccessibility +
                "/abstract=" + current.IsAbstract + "/static=" + current.IsStatic + "/ref=" + current.IsRefLikeType);
            for (var owner = current; owner != null; owner = owner.ContainingType)
                hierarchy.Add(Identity(owner) + "/constraints=" + Constraints(owner.TypeParameters));
        }
        var model = new MarkedHandlerModel(Identity(type), Name(type), new(hierarchy),
            new(contracts.OrderBy(c => c.Contract, StringComparer.Ordinal).ThenBy(c => c.Request, StringComparer.Ordinal)
                .ThenBy(c => c.Result, StringComparer.Ordinal)),
            (markerRequired && attributes.Length != 1) || type.TypeKind is not (TypeKind.Class or TypeKind.Struct));
        return new(model, type, attributes.FirstOrDefault()?.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? Location.None);
    }

    internal static MarkedHandlerCatalog Normalize(ImmutableArray<Candidate> candidates)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var handlers = candidates.Where(c => seen.Add(c.Symbol)).Select(c => c.Model)
            .OrderBy(m => m.Identity, StringComparer.Ordinal).ToArray();
        return new(new(handlers));
    }

    // Captures assembly identity recursively, including constructed/nested arguments.
    // This is a value shape; CLR registration equivalence and catalog fingerprints are separate.
    internal static string Identity(ITypeSymbol type) => type switch
    {
        INamedTypeSymbol named => "named(" + Part(named.ContainingAssembly?.Identity.ToString() ?? "") +
            Part(QualifiedMetadataName(named.OriginalDefinition) ?? Name(named)) +
            Part(named.ContainingType == null ? "" : Identity(named.ContainingType)) +
            string.Join("", named.TypeArguments.Select(t => Part(Identity(t)))) + ")/" + named.NullableAnnotation,
        IArrayTypeSymbol array => "array(" + array.Rank + Part(Identity(array.ElementType)) + ")/" + array.NullableAnnotation,
        IPointerTypeSymbol pointer => "pointer(" + Part(Identity(pointer.PointedAtType)) + ")",
        ITypeParameterSymbol parameter => "parameter(" + Part(parameter.ContainingSymbol.ContainingAssembly?.Identity.ToString() ?? "") +
            Part(parameter.ContainingSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)) +
            parameter.TypeParameterKind + ":" + parameter.Ordinal + ")/" + parameter.NullableAnnotation,
        _ => type.TypeKind + "(" + Part(Name(type)) + ")/" + type.NullableAnnotation
    };

    private static string Part(string value) => value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value;

    // CLR registration/route equivalence is separate from the public nullable/dynamic shape.
    internal static string RegistrationIdentity(ITypeSymbol type, Compilation compilation) => type switch
    {
        IDynamicTypeSymbol => RegistrationIdentity(compilation.GetSpecialType(SpecialType.System_Object), compilation),
        INamedTypeSymbol named => "named(" + Part(named.ContainingAssembly?.Identity.ToString() ?? "") +
            Part(QualifiedMetadataName(named.OriginalDefinition) ?? Name(named)) +
            Part(named.ContainingType == null ? "" : RegistrationIdentity(named.ContainingType, compilation)) +
            string.Join("", named.TypeArguments.Select(t => Part(RegistrationIdentity(t, compilation)))) + ")",
        IArrayTypeSymbol array => "array(" + array.Rank + Part(RegistrationIdentity(array.ElementType, compilation)) + ")",
        IPointerTypeSymbol pointer => "pointer(" + Part(RegistrationIdentity(pointer.PointedAtType, compilation)) + ")",
        _ => Identity(type)
    };

    internal static bool HasOpenShape(ITypeSymbol type) => HasOpenArguments(type) ||
        (type is INamedTypeSymbol { ContainingType: { } owner } && HasOpenShape(owner));

    internal static string Constraints(ImmutableArray<ITypeParameterSymbol> parameters) => string.Join(";", parameters.Select(p =>
        p.Ordinal + ":" + p.Name + ":" + p.Variance + ":" + p.HasReferenceTypeConstraint + ":" + p.ReferenceTypeConstraintNullableAnnotation +
        ":" + p.HasValueTypeConstraint + ":" + p.HasUnmanagedTypeConstraint + ":" + p.HasNotNullConstraint +
        ":" + p.HasConstructorConstraint + ":" + p.AllowsRefLikeType + ":" + string.Join(",", p.ConstraintTypes.Select(Identity))));
}
