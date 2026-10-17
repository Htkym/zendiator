using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
namespace Zendiator.Generator.Tests;

// Independent reference for the v1 catalog encoding and product contracts.
internal static class CatalogV1Reference
{
    private const string Algorithm = "zendiator.handler-contract/1";
    private static string Definition(INamedTypeSymbol type)
    {
        var names = new Stack<string>(); for (var t = type.OriginalDefinition; t != null; t = t.ContainingType) names.Push(t.MetadataName);
        return type.ContainingAssembly.Identity + "|" + (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + string.Join("+", names);
    }
    private static string TypeId(ITypeSymbol type, bool annotations = true) => type switch
    {
        IDynamicTypeSymbol => JsonSerializer.Serialize(new[] { "object", annotations && type.NullableAnnotation == NullableAnnotation.Annotated ? "2" : "0" }),
        INamedTypeSymbol { SpecialType: SpecialType.System_Object } => JsonSerializer.Serialize(new[] { "object", annotations && type.NullableAnnotation == NullableAnnotation.Annotated ? "2" : "0" }),
        ITypeParameterSymbol p => JsonSerializer.Serialize(new[] { "parameter", Definition((INamedTypeSymbol)p.ContainingSymbol), p.Ordinal.ToString(), (annotations ? (int)p.NullableAnnotation : 0).ToString() }),
        IArrayTypeSymbol a => JsonSerializer.Serialize(new[] { "array", a.Rank.ToString(), TypeId(a.ElementType, annotations), (annotations ? (int)a.NullableAnnotation : 0).ToString() }),
        INamedTypeSymbol n => JsonSerializer.Serialize(new[] { "named", Definition(n), n.ContainingType is { } o ? TypeId(o, annotations) : "", (annotations ? (int)n.NullableAnnotation : 0).ToString() }.Concat(n.TypeArguments.Select(t => TypeId(t, annotations)))),
        _ => throw new NotSupportedException()
    };
    internal static string Fingerprint(Compilation compilation, INamedTypeSymbol type)
    {
        var rows = new List<string> { Algorithm, TypeId(type), type.DeclaredAccessibility.ToString(), type.TypeKind.ToString(), type.IsAbstract.ToString(), type.IsStatic.ToString(), type.IsRefLikeType.ToString() };
        var owners = new Stack<INamedTypeSymbol>(); for (var t = type; t != null; t = t.ContainingType) owners.Push(t);
        foreach (var p in owners.SelectMany(t => t.TypeParameters.OrderBy(p => p.Ordinal)))
            rows.Add(JsonSerializer.Serialize(new object[] { Definition((INamedTypeSymbol)p.ContainingSymbol), p.Ordinal, p.Variance.ToString(), p.HasReferenceTypeConstraint, p.ReferenceTypeConstraintNullableAnnotation.ToString(), p.HasValueTypeConstraint, p.HasUnmanagedTypeConstraint, p.HasNotNullConstraint, p.HasConstructorConstraint, p.AllowsRefLikeType, p.ConstraintTypes.Select(t => TypeId(t)).OrderBy(t => t, StringComparer.Ordinal).ToArray() }));
        var assembly = compilation.GetTypeByMetadataName("Zendiator.IRequestHandler\u00602")!.ContainingAssembly;
        var names = new[] { ("Zendiator.IRequestHandler\u00602", "SendAsync"), ("Zendiator.IRequestHandler\u00601", "SendAsync"), ("Zendiator.ISyncRequestHandler\u00602", "SendSync"), ("Zendiator.ISyncRequestHandler\u00601", "SendSync"), ("Zendiator.IStreamRequestHandler\u00602", "Stream"), ("Zendiator.INotificationHandler\u00601", "Notification") };
        rows.AddRange(type.AllInterfaces.SelectMany(i => names.Where(n => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, assembly.GetTypeByMetadataName(n.Item1))).Select(n => JsonSerializer.Serialize(new[] { n.Item2, TypeId(i.TypeArguments[0]), i.TypeArguments.Length == 1 ? "void" : TypeId(i.TypeArguments[1]) }))).OrderBy(s => s, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));
    }
}
