using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zendiator.SourceGenerator;

internal sealed record CanonicalMediatorApi(string Namespace, EquatableArray<string> Methods, EquatableArray<string> Externs);
internal sealed record CanonicalMediatorCallSite(string Key, int Version, string Data, string Receiver, string Concrete,
    string Result, string Method, string TypeParameters, string Constraints, string Parameters, string Arguments,
    EquatableArray<string> Externs);

internal static class CanonicalMediatorCallSites
{
    internal static bool Candidate(SyntaxNode node) => node is InvocationExpressionSyntax
    {
        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Send" or "SendAsync" or "SendAll" or "SendAllAsync" or "StreamAsync" or "StreamSource" }
    };

    internal static CanonicalMediatorApi Api(CanonicalSourceFile file)
    {
        if (file.Api is { } cached) return cached;
        var root = CSharpSyntaxTree.ParseText(file.Source).GetCompilationUnitRoot();
        var ns = root.Members.OfType<NamespaceDeclarationSyntax>().Single();
        var api = ns.Members.OfType<InterfaceDeclarationSyntax>().Single();
        return new(ns.Name.ToString(), new(api.Members.OfType<MethodDeclarationSyntax>().Select(m => m.NormalizeWhitespace().ToFullString())),
            new(root.Externs.Select(e => e.WithLeadingTrivia().WithTrailingTrivia().ToString())));
    }

    internal static EquatableArray<CanonicalMediatorCallSite> Bind(ImmutableArray<InvocationExpressionSyntax> calls,
        EquatableArray<CanonicalMediatorApi> apis, Compilation compilation, CancellationToken token)
    {
        if (calls.Length == 0 || apis.Count == 0) return default;
        var externs = new EquatableArray<string>(apis.SelectMany(a => a.Externs).Distinct(StringComparer.Ordinal));
        var stub = new StringBuilder("#nullable enable\n");
        foreach (var declaration in externs) stub.AppendLine(declaration);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in apis.GroupBy(a => a.Namespace, StringComparer.Ordinal))
        {
            // A user declaration with the generated identity is an error, never an interception target.
            if (compilation.GetTypeByMetadataName(group.Key + ".IZendiator") != null || compilation.GetTypeByMetadataName(group.Key + ".Zendiator") != null) continue;
            namespaces.Add(group.Key);
            var methods = group.SelectMany(a => a.Methods).Distinct(StringComparer.Ordinal).Select(m => (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(m)!).ToArray();
            stub.Append("namespace ").Append(group.Key).AppendLine(" { public interface IZendiator {");
            foreach (var method in methods) stub.AppendLine(method.ToFullString());
            stub.AppendLine("} public sealed class Zendiator : IZendiator, global::System.IServiceProvider { public Zendiator(global::System.IServiceProvider provider) {} ");
            stub.AppendLine("object? global::System.IServiceProvider.GetService(global::System.Type serviceType) => throw null!;");
            foreach (var method in methods) stub.AppendLine(method.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithSemicolonToken(default).WithBody(SyntaxFactory.Block(SyntaxFactory.ThrowStatement(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)))).NormalizeWhitespace().ToFullString());
            stub.AppendLine("} }");
        }
        if (namespaces.Count == 0) return default;
        // Bind only the targeted invocations against lightweight API declarations; do not walk all trees semantically.
        var options = (CSharpParseOptions?)compilation.SyntaxTrees.FirstOrDefault()?.Options;
        var augmented = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(stub.ToString(), options, cancellationToken: token));
        var output = new List<CanonicalMediatorCallSite>();
        foreach (var call in calls)
        {
            token.ThrowIfCancellationRequested();
            var semantic = augmented.GetSemanticModel(call.SyntaxTree);
            if (semantic.GetSymbolInfo(call, token).Symbol is not IMethodSymbol { IsStatic: false } bound ||
                bound.ContainingType.ContainingType != null || bound.ContainingType.Name is not ("IZendiator" or "Zendiator") ||
                !namespaces.Contains(bound.ContainingNamespace.ToDisplayString())) continue;
            // Keep constrained calls on type parameters intact: converting a struct receiver to
            // IZendiator would box a copy and can change mutation and ref receiver semantics.
            var receiverType = semantic.GetTypeInfo(((MemberAccessExpressionSyntax)call.Expression).Expression, token).Type;
            if (receiverType is not INamedTypeSymbol { IsReferenceType: true }) continue;
            var location = semantic.GetInterceptableLocation(call, token);
            if (location == null) continue;
            var method = bound.OriginalDefinition;
            string Display(ITypeSymbol type) => CanonicalSymbolName.Display(type, augmented);
            var receiver = Display(method.ContainingType);
            var concrete = "global::" + method.ContainingNamespace.ToDisplayString() + ".Zendiator";
            var typeParameters = method.TypeParameters.Length == 0 ? "" : "<" + string.Join(", ", method.TypeParameters.Select(p => p.Name)) + ">";
            var constraints = string.Concat(method.TypeParameters.Select(p => Constraints(p, augmented)));
            string Ref(IParameterSymbol p) => p.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", RefKind.RefReadOnlyParameter => "ref readonly ", _ => "" };
            var parameters = string.Join(", ", method.Parameters.Select((p, i) => (p.IsParams ? "params " : "") +
                (p.ScopedKind != ScopedKind.None ? "scoped " : "") + Ref(p) + Display(p.Type) + " p" + i));
            var arguments = string.Join(", ", method.Parameters.Select((p, i) => (p.RefKind is RefKind.In or RefKind.RefReadOnlyParameter ? "in " : Ref(p)) + "p" + i));
            output.Add(new(CanonicalSplitSourceEmitter.Id(location.Version + "|" + location.Data), location.Version, location.Data,
                receiver, concrete, method.ReturnsVoid ? "void" : Display(method.ReturnType), method.Name,
                typeParameters, constraints, parameters, arguments, externs));
        }
        return new(output.OrderBy(c => c.Key, StringComparer.Ordinal));
    }

    private static string Constraints(ITypeParameterSymbol parameter, Compilation compilation)
    {
        var constraints = new List<string>();
        if (parameter.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
        else if (parameter.HasValueTypeConstraint) constraints.Add("struct");
        else if (parameter.HasReferenceTypeConstraint) constraints.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        else if (parameter.HasNotNullConstraint) constraints.Add("notnull");
        constraints.AddRange(parameter.ConstraintTypes.Select(t => CanonicalSymbolName.Display(t, compilation)));
        if (parameter.HasConstructorConstraint) constraints.Add("new()");
        if (parameter.AllowsRefLikeType) constraints.Add("allows ref struct");
        return constraints.Count == 0 ? "" : " where " + parameter.Name + " : " + string.Join(", ", constraints);
    }

    internal static CanonicalSourceFile Emit(CanonicalMediatorCallSite call)
    {
        var b = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        foreach (var declaration in call.Externs) b.AppendLine(declaration);
        b.AppendLine($$"""
            namespace System.Runtime.CompilerServices {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                file sealed class InterceptsLocationAttribute : global::System.Attribute {
                    public InterceptsLocationAttribute(int version, string data) { }
                }
            }
            namespace Zendiator.Generated.Interceptors {
                internal static class Dispatch_{{call.Key}} {
                    [global::System.Runtime.CompilerServices.InterceptsLocation({{call.Version}}, "{{call.Data.Replace("\\", "\\\\").Replace("\"", "\\\"")}}")]
                    public static {{call.Result}} Intercept{{call.TypeParameters}}(this {{call.Receiver}} mediator, {{call.Parameters}}){{call.Constraints}}
                    {
            """);
        var invocation = call.Method + call.TypeParameters + "(" + call.Arguments + ")";
        if (call.Receiver == call.Concrete) b.AppendLine((call.Result == "void" ? "" : "return ") + "mediator." + invocation + ";");
        else b.AppendLine("if (mediator is " + call.Concrete + " concrete) { " + (call.Result == "void" ? "concrete." + invocation + "; return;" : "return concrete." + invocation + ";") + " }\n" +
            (call.Result == "void" ? "" : "return ") + "mediator." + invocation + ";");
        b.AppendLine("} } }");
        return new("Zendiator.Dispatch." + call.Key + ".g.cs", b.ToString());
    }
}
