using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

// The legacy contract still supplies the stage, item boundary and DI service identity.
internal static class CanonicalStructStreamContractBinding
{
    internal static BoundStructStreamCallEmission? Bind(INamedTypeSymbol receiver, INamedTypeSymbol legacy,
        Compilation compilation, out string? error, out bool hasTypedImplementation)
    {
        error = null;
        hasTypedImplementation = false;
        var assembly = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        var definition = assembly?.GetTypeByMetadataName(ContractNames.StructStreamMiddleware);
        var contracts = receiver.AllInterfaces.Where(i => Same(i.OriginalDefinition, definition) &&
            i.TypeArguments.Length == 2 && i.TypeArguments.Zip(legacy.TypeArguments, (left, right) =>
                SymbolEqualityComparer.IncludeNullability.Equals(left, right)).All(equal => equal)).ToArray();
        var contract = contracts.Length == 0 ? legacy : contracts.Length == 1 ? contracts[0] : null;
        var member = contract?.GetMembers().OfType<IMethodSymbol>().SingleOrDefault(m => m.Arity == 1 && m.MethodKind == MethodKind.Ordinary);
        if (member == null && contracts.Length == 0)
            return null;
        var method = member == null ? null : receiver.FindImplementationForInterfaceMember(member) as IMethodSymbol;
        // A legacy-only receiver uses the standard interface adapter; keep its existing dispatch.
        if (contracts.Length == 0 && Same(method, member))
            return null;
        if (method == null || !Signature(method, legacy, compilation))
        {
            error = "IStructStreamMiddleware requires OpenAsync<TNext>(request, TNext next, token) returning " +
                "ValueTask<IAsyncEnumerator<TItem>>, with exactly where TNext : struct, IStreamUpstream<TRequest,TItem>.";
            return null;
        }
        hasTypedImplementation = !Same(method, member);
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
        return new(method.Name, ForwardsUpstream(method, compilation));
    }

    private static bool ForwardsUpstream(IMethodSymbol method, Compilation compilation)
    {
        var syntax = method.DeclaringSyntaxReferences;
        if (method.IsAsync || method.PartialDefinitionPart != null || method.PartialImplementationPart != null ||
            syntax.Length != 1 || syntax[0].GetSyntax() is not MethodDeclarationSyntax declaration ||
            declaration.Modifiers.Any(SyntaxKind.UnsafeKeyword) || !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            return false;
        var semantic = compilation.GetSemanticModel(declaration.SyntaxTree);
        var tail = declaration.ExpressionBody?.Expression;
        if (declaration.Body is { } body)
        {
            if (body.Statements.LastOrDefault() is not ReturnStatementSyntax last)
                return false;
            tail = last.Expression;
            foreach (var statement in body.Statements.Take(body.Statements.Count - 1))
            {
                // Keep preprocessing calls, but exclude port escapes and changes to the forwarded arguments.
                if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call } ||
                    call.DescendantNodes().Any(n => n is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or
                        ArgumentListSyntax or ArgumentSyntax or LiteralExpressionSyntax)) ||
                    semantic.GetOperation(call) is not IInvocationOperation preprocessing ||
                    preprocessing.TargetMethod.IsExtensionMethod ||
                    preprocessing.TargetMethod.Parameters.Any(p => p.RefKind != RefKind.None) ||
                    call.ArgumentList.Arguments.Any(a => a.RefKindKeyword.RawKind != 0) ||
                    call.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id =>
                        id.Identifier.ValueText == method.Parameters[1].Name || id.Identifier.ValueText == method.Parameters[2].Name ||
                        id.Identifier.ValueText == method.TypeParameters[0].Name))
                    return false;
            }
        }
        if (tail is not InvocationExpressionSyntax invocation)
            return false;
        if (semantic.GetInterceptorMethod(invocation) != null || semantic.GetOperation(invocation) is not IInvocationOperation callNext ||
            callNext.Instance is not IParameterReferenceOperation { Parameter.Ordinal: 1 } ||
            callNext.TargetMethod.Name != "OpenAsync" ||
            !Same(callNext.TargetMethod.ContainingType.OriginalDefinition, compilation.GetTypeByMetadataName("Zendiator.IStreamUpstream`2")) ||
            callNext.Arguments.Length != 2)
            return false;
        return callNext.Arguments.All(a => a.Value is IParameterReferenceOperation parameter &&
            parameter.Parameter.Ordinal == (a.Parameter?.Ordinal == 0 ? 0 : 2));
    }

    private static bool Signature(IMethodSymbol method, INamedTypeSymbol legacy, Compilation compilation)
    {
        if (method.IsStatic || method.Arity != 1 || method.Parameters.Length != 3 ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.Parameters.Any(p => p.RefKind != RefKind.None || p.ScopedKind != ScopedKind.None) ||
            !Same(method.ReturnType, legacy.GetMembers().OfType<IMethodSymbol>().Single(m => m.Arity == 0).ReturnType) ||
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
