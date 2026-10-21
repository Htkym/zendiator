using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace Zendiator.SourceGenerator;

// Gives route-private members stable names without a second composition backend.
internal static class CanonicalMultiSourceEmitter
{
    internal class RouteNames : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _methods; private readonly Dictionary<string, string> _types;
        internal RouteNames(ClassDeclarationSyntax? type, string suffix)
        {
            _methods = type == null ? new(StringComparer.Ordinal) : type.Members.OfType<MethodDeclarationSyntax>().Where(m => m.Modifiers.Any(SyntaxKind.PrivateKeyword)).ToDictionary(m => m.Identifier.ValueText, m => m.Identifier.ValueText + suffix, StringComparer.Ordinal);
            _types = type == null ? new(StringComparer.Ordinal) : type.Members.OfType<StructDeclarationSyntax>().ToDictionary(s => s.Identifier.ValueText, s => s.Identifier.ValueText + suffix, StringComparer.Ordinal);
        }
        private static SyntaxToken Rename(SyntaxToken token, string value) => SyntaxFactory.Identifier(token.LeadingTrivia, value, token.TrailingTrivia);
        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        { var result = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!; return node.Modifiers.Any(SyntaxKind.PrivateKeyword) && _methods.TryGetValue(node.Identifier.ValueText, out var name) ? result.WithIdentifier(Rename(result.Identifier, name)) : result; }
        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
        { var result = (StructDeclarationSyntax)base.VisitStructDeclaration(node)!; return _types.TryGetValue(node.Identifier.ValueText, out var name) ? result.WithIdentifier(Rename(result.Identifier, name)) : result; }
        public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        { var result = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!; return _types.TryGetValue(node.Identifier.ValueText, out var name) ? result.WithIdentifier(Rename(result.Identifier, name)) : result; }
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Parent is not (QualifiedNameSyntax or AliasQualifiedNameSyntax or MemberAccessExpressionSyntax) && _types.TryGetValue(node.Identifier.ValueText, out var type)) return node.WithIdentifier(Rename(node.Identifier, type));
            if (node.Parent is InvocationExpressionSyntax call && ReferenceEquals(call.Expression, node) && _methods.TryGetValue(node.Identifier.ValueText, out var method)) return node.WithIdentifier(Rename(node.Identifier, method));
            return base.VisitIdentifierName(node);
        }
        public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
        {
            var result = (GenericNameSyntax)base.VisitGenericName(node)!;
            if (node.Parent is not (QualifiedNameSyntax or AliasQualifiedNameSyntax or MemberAccessExpressionSyntax) && _types.TryGetValue(node.Identifier.ValueText, out var type))
                return result.WithIdentifier(Rename(result.Identifier, type));
            return node.Parent is InvocationExpressionSyntax call && ReferenceEquals(call.Expression, node) && _methods.TryGetValue(node.Identifier.ValueText, out var method)
                ? result.WithIdentifier(Rename(result.Identifier, method)) : result;
        }
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var result = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            return node.Expression is IdentifierNameSyntax { Identifier.ValueText: "_owner" } && _methods.TryGetValue(node.Name.Identifier.ValueText, out var method) ?
                result.WithName(result.Name.WithIdentifier(Rename(result.Name.Identifier, method))) : result;
        }
    }
}
