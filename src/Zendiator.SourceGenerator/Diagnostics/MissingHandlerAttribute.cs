using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Zendiator.Tooling;

// Shared with the independent CodeFix assembly. This file must remain compiler-only.
internal static class MissingHandlerAttribute
{
    internal const string ReasonKey = "Zendiator.Reason";
    internal const string Reason = "MissingHandlerAttribute";
    internal const string HandlerKey = "Zendiator.Handler";
    internal const string SourceChecksumKey = "Zendiator.SourceChecksum";
    internal const string TargetChecksumKey = "Zendiator.TargetChecksum";
    internal const string AttributeName = "Zendiator.ZendiatorHandlerAttribute";

    internal sealed class Target
    {
        internal Target(INamedTypeSymbol handler, ClassDeclarationSyntax declaration, SyntaxTree source, CancellationToken ct)
        {
            Declaration = declaration;
            Properties = ImmutableDictionary<string, string?>.Empty
                .Add(ReasonKey, Reason)
                .Add(HandlerKey, DocumentationCommentId.CreateDeclarationId(handler))
                .Add(SourceChecksumKey, Checksum(source, ct))
                .Add(TargetChecksumKey, Checksum(declaration.SyntaxTree, ct));
        }
        internal ClassDeclarationSyntax Declaration { get; }
        internal ImmutableDictionary<string, string?> Properties { get; }
    }

    internal static Target? Find(Compilation compilation, SyntaxTree source, TextSpan span, CancellationToken ct)
    {
        if (!compilation.ContainsSyntaxTree(source)) return null;
        var model = compilation.GetSemanticModel(source);
        var root = source.GetRoot(ct);
        if (!root.FullSpan.Contains(span)) return null;
        var expression = root.FindNode(span, getInnermostNodeForTie: true) as TypeOfExpressionSyntax;
        if (expression?.Span != span || expression.Parent is not ArgumentSyntax argument ||
            argument.Parent?.Parent is not InvocationExpressionSyntax call ||
            model.GetSymbolInfo(call, ct).Symbol is not IMethodSymbol { Name: "Handlers", Arity: 1 } method) return null;

        var contractAssembly = ReferencedAssembly(compilation, "Zendiator.Abstractions");
        var runtimeAssembly = ReferencedAssembly(compilation, "Zendiator");
        var configuration = runtimeAssembly?.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorConfiguration");
        var bootstrap = runtimeAssembly?.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions");
        var marker = contractAssembly?.GetTypeByMetadataName(AttributeName);
        var familyType = contractAssembly?.GetTypeByMetadataName("Zendiator.DispatchFamily");
        if (marker is null || configuration is null || bootstrap is null || familyType is null ||
            !Same(compilation.GetTypeByMetadataName(AttributeName), marker) || !Same(method.ContainingType, configuration) ||
            call.ArgumentList.Arguments.Count < 2 || call.ArgumentList.Arguments.Any(a => a.NameColon != null || a.RefKindKeyword != default) ||
            call.ArgumentList.Arguments.Skip(1).Any(a => a.Expression is not TypeOfExpressionSyntax) ||
            !Same(model.GetTypeInfo(call.ArgumentList.Arguments[0].Expression, ct).Type, familyType) ||
            model.GetConstantValue(call.ArgumentList.Arguments[0].Expression, ct) is not { HasValue: true, Value: int family } || family < 0 || family > 4 ||
            method.TypeArguments[0] is not INamedTypeSymbol { Arity: 0, TypeKind: not TypeKind.Error } request ||
            model.GetTypeInfo(expression.Type, ct).Type is not INamedTypeSymbol
            { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false, IsSealed: true, Arity: 0, ContainingType: null, DeclaredAccessibility: Accessibility.Public } handler ||
            !Same(handler.ContainingAssembly, compilation.Assembly) || handler.BaseType?.SpecialType != SpecialType.System_Object ||
            handler.GetAttributes().Any(a => a.AttributeClass?.Name == "ZendiatorHandlerAttribute")) return null;

        var declarations = handler.DeclaringSyntaxReferences.Select(r => r.GetSyntax(ct)).ToArray();
        if (declarations.Length == 0 || declarations.Any(d => d is not ClassDeclarationSyntax || d.SyntaxTree != declarations[0].SyntaxTree)) return null;
        var declaration = (ClassDeclarationSyntax)declarations.OrderBy(d => d.SpanStart).First();
        var targetModel = compilation.GetSemanticModel(declaration.SyntaxTree);
        if (declarations.Any(d => d.ContainsDiagnostics || targetModel.GetDiagnostics(d.Span, ct).Any(e => e.Severity == DiagnosticSeverity.Error))) return null;

        var names = family switch
        {
            0 or 2 => new[] { "Zendiator.IRequestHandler`2", "Zendiator.IRequestHandler`1" },
            1 or 3 => new[] { "Zendiator.ISyncRequestHandler`2", "Zendiator.ISyncRequestHandler`1" },
            _ => new[] { "Zendiator.IStreamRequestHandler`2" }
        };
        var definitions = names.Select(n => contractAssembly!.GetTypeByMetadataName(n)).ToArray();
        var contracts = handler.AllInterfaces.Where(i => definitions.Any(d => Same(i.OriginalDefinition, d)) && Same(i.TypeArguments[0], request)).ToArray();
        if (contracts.Length != 1 || contracts[0].GetMembers().OfType<IMethodSymbol>().Any(m =>
            handler.FindImplementationForInterfaceMember(m) is not IMethodSymbol { IsAbstract: false })) return null;
        if (call.ArgumentList.Arguments.Skip(1).Count(a => Same(model.GetTypeInfo(((TypeOfExpressionSyntax)a.Expression).Type, ct).Type, handler)) != 1) return null;
        if (!ExplicitCompositionsOnly(compilation, configuration, bootstrap, contractAssembly!, call, ct)) return null;
        return new Target(handler, declaration, source, ct);
    }

    private static bool ExplicitCompositionsOnly(Compilation compilation, INamedTypeSymbol configuration, INamedTypeSymbol bootstrap,
        IAssemblySymbol contracts, InvocationExpressionSyntax selected, CancellationToken ct)
    {
        var compositionMarker = contracts.GetTypeByMetadataName("Zendiator.ZendiatorCompositionAttribute");
        var legacyMarker = contracts.GetTypeByMetadataName("Zendiator.GenerateZendiatorAttribute");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var found = false;
        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var root = tree.GetRoot(ct);
            if (root.ContainsDiagnostics) return false;
            var model = compilation.GetSemanticModel(tree);
            foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
                if (model.GetSymbolInfo(attribute, ct).Symbol is IMethodSymbol constructor && Same(constructor.ContainingType, legacyMarker)) return false;
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                ct.ThrowIfCancellationRequested();
                if (model.GetSymbolInfo(call, ct).Symbol is not IMethodSymbol method) continue;
                if (Same(method.ContainingType, configuration) && method.Name is "UseLocalHandlers" or "UseCatalogFrom" or "AddHandler") return false;
                if (!Same(method.ContainingType, bootstrap) || method.Name != "AddZendiator") continue;
                if (method.Arity != 1 || method.TypeArguments[0] is not INamedTypeSymbol
                    { TypeKind: TypeKind.Class, IsSealed: true, IsStatic: false, Arity: 0, ContainingType: null, DeclaredAccessibility: Accessibility.Public } marker ||
                    !Same(marker.ContainingAssembly, compilation.Assembly) ||
                    call.ArgumentList.Arguments.LastOrDefault()?.Expression is not SimpleLambdaExpressionSyntax { Body: BlockSyntax body } lambda ||
                    lambda.AsyncKeyword != default) return false;
                var attributes = marker.GetAttributes().Where(a => Same(a.AttributeClass, compositionMarker)).ToArray();
                if (attributes.Length != 1 || attributes[0].ConstructorArguments.FirstOrDefault().Value is not string key ||
                    string.IsNullOrWhiteSpace(key) || !keys.Add(key)) return false;
                var parameter = model.GetDeclaredSymbol(lambda.Parameter, ct);
                var routes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var statement in body.Statements)
                {
                    if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax recorder } ||
                        recorder.Expression is not MemberAccessExpressionSyntax access ||
                        !Same(model.GetSymbolInfo(access.Expression, ct).Symbol, parameter) ||
                        model.GetSymbolInfo(recorder, ct).Symbol is not IMethodSymbol { Arity: 1 } record ||
                        !Same(record.ContainingType, configuration) || record.Name is not ("Handlers" or "Pipeline")) return false;
                    if (record.Name == "Handlers")
                    {
                        if (recorder.ArgumentList.Arguments.Count < 2 || !model.GetConstantValue(recorder.ArgumentList.Arguments[0].Expression, ct).HasValue ||
                            !routes.Add(record.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" +
                                model.GetConstantValue(recorder.ArgumentList.Arguments[0].Expression, ct).Value)) return false;
                    }
                    if (recorder == selected) found = true;
                }
            }
        }
        return found;
    }

    private static IAssemblySymbol? ReferencedAssembly(Compilation compilation, string name)
    {
        var matches = compilation.SourceModule.ReferencedAssemblySymbols.Where(a => a.Name == name).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private static bool Same(ISymbol? left, ISymbol? right) => left != null && right != null && SymbolEqualityComparer.Default.Equals(left, right);
    private static string Checksum(SyntaxTree tree, CancellationToken ct)
    {
        using var hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(tree.GetText(ct).ToString())));
    }
}
