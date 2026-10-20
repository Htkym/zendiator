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
        var call = Assert.IsType<InvocationExpressionSyntax>(declaration.ExpressionBody!.Expression);
        var target = Assert.IsAssignableFrom<IMethodSymbol>(output.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol);
        Assert.True(SymbolEqualityComparer.Default.Equals(facadeEntry.ContainingType, target.ContainingType.ContainingType));
        Assert.Equal(facadeEntry.Name, target.Name);
        Assert.Equal(facadeEntry.TypeParameters.Cast<ITypeSymbol>(), target.TypeArguments, SymbolEqualityComparer.Default);
        return target.OriginalDefinition;
    }
}
