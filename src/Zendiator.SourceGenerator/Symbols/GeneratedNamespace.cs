using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Zendiator.SourceGenerator.GeneratorDiagnostics;

namespace Zendiator.SourceGenerator;

internal static class GeneratedNamespace
{
    internal static string? ResolveAssemblyNamespace(AttributeData assemblyGen, Compilation compilation, List<Diagnostic> errors)
    {
        void Error(string message) => errors.Add(Diagnostic.Create(Rules[6], Location.None, message));
        string? ns = null;
        foreach (var pair in assemblyGen.NamedArguments)
        {
            if (pair.Key == "Namespace" && pair.Value.Value is string s && !string.IsNullOrWhiteSpace(s))
                ns = s.Trim();
        }
        ns ??= DefaultGeneratedNamespace(compilation.AssemblyName);
        if (ns == null || !IsValidNamespace(ns))
        {
            Error($"Invalid generation namespace '{ns ?? "<empty>"}'. Set [assembly: GenerateZendiator(Namespace = \"MyApp.Generated\")] with dot-separated identifiers.");
            return null;
        }
        foreach (var name in new[] { "Zendiator", "IZendiator", "ZendiatorServiceCollectionExtensions" })
        {
            if (compilation.GetTypeByMetadataName(ns + "." + name) != null)
            {
                Error($"Generated name collision: '{ns}.{name}' already exists. Choose a different Namespace.");
                return null;
            }
        }
        return ns;
    }

    internal static string? DefaultGeneratedNamespace(string? assemblyName)
    {
        var name = assemblyName;
        if (name is not { Length: > 0 } || string.IsNullOrWhiteSpace(name)) return null;
        var clean = name.Split('.').Select(static part =>
        {
            var chars = part.Select(static c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray();
            var text = chars.Length == 0 ? "_" : new string(chars);
            return text.Length != 0 && (char.IsLetter(text[0]) || text[0] == '_') ? text : "_" + text;
        }).ToArray();
        return string.Join(".", clean) + ".Generated";
    }

    internal static bool IsValidNamespace(string ns)
    {
        var parts = ns.Split('.');
        if (parts.Length == 0) return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || !SyntaxFacts.IsValidIdentifier(part)) return false;
        }
        return true;
    }
}
