using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Zendiator.SourceGenerator;

// Assembly/type identity stays in the binding model. Only the available C# reference path changes.
internal static class CanonicalSymbolName
{
    internal static EquatableArray<string> Aliases(Compilation compilation) => new(compilation.References
        .SelectMany(r => r.Properties.Aliases.IsDefaultOrEmpty ? Enumerable.Empty<string>() : r.Properties.Aliases).Where(a => a != "global" &&
            (SyntaxFacts.IsValidIdentifier(a) || SyntaxFacts.GetKeywordKind(a) != SyntaxKind.None))
        .Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal));

    private static string Identifier(string value) => SyntaxFacts.GetKeywordKind(value) == SyntaxKind.None ? value : "@" + value;

    internal static string Declarations(IEnumerable<string> aliases) => string.Concat(aliases.Distinct(StringComparer.Ordinal)
        .OrderBy(a => a, StringComparer.Ordinal).Select(a => "extern alias " + Identifier(a) + ";\n"));

    internal static string Display(ITypeSymbol type, Compilation compilation, IReadOnlyDictionary<ITypeParameterSymbol, string>? parameters = null)
    {
        if (parameters == null && !compilation.References.Any(r => !r.Properties.Aliases.IsDefaultOrEmpty && r.Properties.Aliases.Any(a => a != "global")))
            return SymbolUtilities.Name(type);
        var parts = type.ToDisplayParts(SymbolUtilities.TypeFormat);
        var text = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Symbol is ITypeParameterSymbol parameter && parameters != null && parameters.TryGetValue(parameter, out var replacement))
            { text.Append(replacement); continue; }
            if (part.ToString() == "global" && i + 1 < parts.Length && parts[i + 1].ToString() == "::")
            {
                // Each qualified named type, including a nested generic argument, has its own prefix.
                var named = parts.Skip(i + 2).Select(p => p.Symbol).OfType<INamedTypeSymbol>().FirstOrDefault();
                var alias = named == null ? null : Alias(named, compilation);
                text.Append(alias == null ? "global" : Identifier(alias));
            }
            else text.Append(part.ToString());
        }
        return text.ToString();
    }

    private static string? Alias(INamedTypeSymbol type, Compilation compilation)
    {
        var definition = type.OriginalDefinition;
        var name = SymbolUtilities.QualifiedMetadataName(definition);
        if (name == null || SymbolUtilities.Same(definition.ContainingAssembly, compilation.Assembly) ||
            SymbolUtilities.Same(compilation.GetTypeByMetadataName(name)?.OriginalDefinition, definition)) return null;
        // Only look up this known type. Do not enumerate assemblies' exported types or interfaces.
        return compilation.References.SelectMany(reference =>
        {
            if (reference.Properties.Aliases.IsDefaultOrEmpty || compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly ||
                !SymbolUtilities.Same((assembly.GetTypeByMetadataName(name) ?? assembly.ResolveForwardedType(name))?.OriginalDefinition, definition))
                return Array.Empty<string>();
            return reference.Properties.Aliases.Where(a => a != "global" &&
                (SyntaxFacts.IsValidIdentifier(a) || SyntaxFacts.GetKeywordKind(a) != SyntaxKind.None));
        }).Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal)
            .FirstOrDefault(alias => ResolvesUniquely(alias, name, definition, compilation));
    }

    private static bool ResolvesUniquely(string alias, string name, INamedTypeSymbol definition, Compilation compilation)
    {
        var found = false;
        foreach (var reference in compilation.References)
        {
            if (reference.Properties.Aliases.IsDefaultOrEmpty || !reference.Properties.Aliases.Contains(alias) ||
                compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly) continue;
            var resolved = assembly.GetTypeByMetadataName(name) ?? assembly.ResolveForwardedType(name);
            if (resolved == null) continue;
            // Every definition exposed by the alias group must be this exact type. Forwarded
            // copies of the same definition are harmless; same-name types in other assemblies are not.
            if (!SymbolUtilities.Same(resolved.OriginalDefinition, definition)) return false;
            found = true;
        }
        return found;
    }
}
