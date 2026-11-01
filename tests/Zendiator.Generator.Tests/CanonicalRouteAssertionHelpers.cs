using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static IEnumerable<string> CanonicalConstraintClauses(IMethodSymbol method) =>
        ((MethodDeclarationSyntax)method.DeclaringSyntaxReferences.Single().GetSyntax()).ConstraintClauses
            .Select(clause => clause.WithoutTrivia().NormalizeWhitespace().ToFullString());

    private static IMethodSymbol CanonicalRouteEntry(Compilation output, IMethodSymbol facadeEntry)
    {
        var declaration = Assert.IsType<MethodDeclarationSyntax>(facadeEntry.DeclaringSyntaxReferences.Single().GetSyntax());
        Assert.True(declaration.Body is not null || declaration.ExpressionBody is not null);
        var target = Assert.IsAssignableFrom<IMethodSymbol>(output.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration));
        Assert.True(SymbolEqualityComparer.Default.Equals(facadeEntry, target));
        Assert.Equal("Zendiator", target.ContainingType.Name);
        return target;
    }

    private static string CanonicalEntrySuffix(IMethodSymbol entry)
    {
        var declaration = Assert.IsType<MethodDeclarationSyntax>(entry.DeclaringSyntaxReferences.Single().GetSyntax());
        var key = entry.ContainingNamespace.ToDisplayString() + "|" + entry.Name + "|" + entry.Arity + "|" + declaration.ParameterList.Parameters[0].Type;
        return "__" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
    }

    private static IEnumerable<IMethodSymbol> CanonicalEntryMethods(IMethodSymbol entry, string prefix) =>
        entry.ContainingType.GetMembers().OfType<IMethodSymbol>().Where(m => m.DeclaredAccessibility == Accessibility.Private &&
            m.Name.StartsWith(prefix, StringComparison.Ordinal) && m.Name.EndsWith(CanonicalEntrySuffix(entry), StringComparison.Ordinal));

    private static IEnumerable<INamedTypeSymbol> CanonicalEntryTypes(IMethodSymbol entry, string prefix) =>
        entry.ContainingType.GetTypeMembers().Where(t => t.Name.StartsWith(prefix, StringComparison.Ordinal) &&
            t.Name.EndsWith(CanonicalEntrySuffix(entry), StringComparison.Ordinal));
}
