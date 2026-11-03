using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Zendiator.SourceGenerator;

/// <summary>
/// An intentionally small, opt-in transformation. A body is emitted only after its syntax,
/// source call graph, every reference to the producer, and its binding in the destination
/// partial declaration have been checked. Unsupported code is an error, never a fallback.
/// </summary>
internal static class PooledProducerGeneration
{
    private const string Marker = "Zendiator.GeneratePooledProducerAttribute";
    private const string InterceptorNamespace = "Zendiator.Generated.Interceptors";
    private static readonly DiagnosticDescriptor Unsupported = GeneratorDiagnostics.Rule("ZEN0040", "Unsupported pooled producer");
    private static readonly DiagnosticDescriptor CallSite = GeneratorDiagnostics.Rule("ZEN0041", "Incomplete pooled producer interception");
    private static readonly DiagnosticDescriptor Validation = GeneratorDiagnostics.Rule("ZEN0042", "Pooled producer binding validation failed");

    internal static void Register(IncrementalGeneratorInitializationContext context)
    {
        var methods = context.SyntaxProvider.ForAttributeWithMetadataName(Marker,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => (MethodDeclarationSyntax)ctx.TargetNode).Collect();
        context.RegisterSourceOutput(methods.Combine(context.CompilationProvider), static (output, input) =>
        {
            foreach (var method in input.Left)
                Generate(output, input.Right, method);
        });
    }

    private static void Generate(SourceProductionContext output, Compilation compilation, MethodDeclarationSyntax declaration)
    {
        var token = output.CancellationToken;
        var semantic = compilation.GetSemanticModel(declaration.SyntaxTree);
        if (semantic.GetDeclaredSymbol(declaration, token) is not IMethodSymbol method)
            return;
        void Error(DiagnosticDescriptor rule, string reason, Location? location = null) => output.ReportDiagnostic(
            Diagnostic.Create(rule, location ?? declaration.Identifier.GetLocation(), "Pooled producer '" + method.Name + "': " + reason));
        if (!ValidateShape(compilation, method, declaration, token, out var reason))
        {
            Error(Unsupported, reason);
            return;
        }
        if (!ValidateBody(semantic, declaration.Body!, token, out reason) ||
            !ValidateCallGraph(compilation, method, declaration.Body!, token, out reason))
        {
            Error(Unsupported, reason);
            return;
        }
        if (!FindCallSites(compilation, method, token, out var sites, out reason, out var badLocation))
        {
            Error(CallSite, reason, badLocation);
            return;
        }
        if (compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder`1") == null)
        {
            Error(Validation, "the target framework must provide PoolingAsyncValueTaskMethodBuilder<T>.");
            return;
        }
        var id = CanonicalSplitSourceEmitter.Id(method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" +
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        var helperName = "__ZendiatorPooled_" + id;
        if (method.ContainingType.GetMembers(helperName).Length != 0 ||
            compilation.SyntaxTrees.Any(tree => tree.GetRoot(token).DescendantTokens().Any(t =>
                t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == helperName)))
        {
            Error(Validation, "the generated helper name collides with an existing member.");
            return;
        }
        var helper = EmitHelper(semantic, declaration, helperName);
        var helperTree = CSharpSyntaxTree.ParseText(SourceText.From(helper, Encoding.UTF8),
            (CSharpParseOptions)declaration.SyntaxTree.Options, "Zendiator.PooledProducer." + id + ".g.cs", token);
        var augmented = compilation.AddSyntaxTrees(helperTree);
        if (!ValidateBinding(augmented, declaration, helperTree, token, out reason))
        {
            Error(Validation, reason);
            return;
        }
        output.AddSource("Zendiator.PooledProducer." + id + ".g.cs", SourceText.From(helper, Encoding.UTF8));
        output.AddSource("Zendiator.PooledProducer.Interceptors." + id + ".g.cs",
            SourceText.From(EmitInterceptors(method, helperName, id, sites), Encoding.UTF8));
    }

    private static bool ValidateShape(Compilation compilation, IMethodSymbol method, MethodDeclarationSyntax declaration,
        CancellationToken token, out string reason)
    {
        reason = "only private, non-generic, non-static async ValueTask<bool> methods without parameters and with a block body are supported.";
        var valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        if (method.DeclaredAccessibility != Accessibility.Private || method.IsStatic || !method.IsAsync ||
            method.IsGenericMethod || method.Parameters.Length != 0 || method.MethodKind != MethodKind.Ordinary ||
            method.IsVirtual || method.IsOverride || method.IsAbstract || method.IsExtern || method.IsPartialDefinition ||
            method.PartialDefinitionPart != null || method.ReturnsByRef || method.ReturnsByRefReadonly || declaration.Body == null ||
            method.ReturnType is not INamedTypeSymbol { TypeArguments.Length: 1 } result ||
            !SymbolEqualityComparer.Default.Equals(result.OriginalDefinition, valueTask) ||
            result.TypeArguments[0].SpecialType != SpecialType.System_Boolean)
            return false;
        reason = "the containing type must be a top-level public or internal non-generic partial class, without unsafe or file-local declarations.";
        var type = method.ContainingType;
        if (type.TypeKind != TypeKind.Class || type.IsRecord || type.IsStatic || type.IsGenericType || type.ContainingType != null ||
            type.IsFileLocal || type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            return false;
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(token) is not ClassDeclarationSyntax part ||
                !part.Modifiers.Any(SyntaxKind.PartialKeyword) || part.Modifiers.Any(SyntaxKind.UnsafeKeyword))
                return false;
        }
        reason = "method attributes other than GeneratePooledProducer, including an existing async builder, are not supported.";
        if (method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() != Marker) ||
            method.GetReturnTypeAttributes().Length != 0 || declaration.Modifiers.Any(SyntaxKind.UnsafeKeyword))
            return false;
        reason = "";
        return true;
    }

    private static bool ValidateBody(SemanticModel semantic, BlockSyntax body, CancellationToken token, out string reason)
    {
        reason = "preprocessor directives in the producer body are not supported.";
        if (body.ContainsDirectives)
            return false;
        foreach (var node in body.DescendantNodesAndSelf())
        {
            token.ThrowIfCancellationRequested();
            // This whitelist is deliberate. In particular no declarations, patterns, conversions,
            // user-defined operators, interpolation, conditional access, or hidden scopes are copied.
            if (node is not (BlockSyntax or TryStatementSyntax or CatchClauseSyntax or FinallyClauseSyntax or
                ExpressionStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax or IfStatementSyntax or
                ElseClauseSyntax or EmptyStatementSyntax or AwaitExpressionSyntax or InvocationExpressionSyntax or
                MemberAccessExpressionSyntax or IdentifierNameSyntax or ThisExpressionSyntax or LiteralExpressionSyntax or
                ParenthesizedExpressionSyntax or ArgumentListSyntax or ArgumentSyntax or NameColonSyntax or
                ObjectCreationExpressionSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax or PredefinedTypeSyntax))
            {
                reason = "syntax '" + node.Kind() + "' is outside the supported producer body subset.";
                return false;
            }
            if (node is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                reason = "ref, out, and in arguments are not supported in the producer body.";
                return false;
            }
            if (node is ExpressionSyntax expression)
            {
                if (semantic.GetConversion(expression, token).IsUserDefined)
                {
                    reason = "user-defined conversions are outside the supported producer body subset.";
                    return false;
                }
                var operation = semantic.GetOperation(expression, token);
                if (operation is IUnaryOperation { OperatorMethod: not null } or IBinaryOperation { OperatorMethod: not null })
                {
                    reason = "user-defined operators are outside the supported producer body subset.";
                    return false;
                }
                var type = semantic.GetTypeInfo(expression, token);
                if (UnsafeType(type.Type) || UnsafeType(type.ConvertedType))
                {
                    reason = "dynamic, pointer, function-pointer, and ref-like values are not supported.";
                    return false;
                }
            }
            if (node is IfStatementSyntax conditional && semantic.GetOperation(conditional, token) is IConditionalOperation
                { Condition: IUnaryOperation { OperatorMethod: not null } })
            {
                reason = "user-defined truth operators are outside the supported producer body subset.";
                return false;
            }
            if (node is AwaitExpressionSyntax awaitExpression && !ValidateAwait(semantic, awaitExpression, out reason))
                return false;
            if (node is InvocationExpressionSyntax invocation &&
                !ValidateInvocation(semantic, invocation, token, out reason))
                return false;
            if (node is ObjectCreationExpressionSyntax creation)
            {
                if (semantic.GetOperation(creation, token) is not IObjectCreationOperation operation ||
                    operation.Constructor == null || operation.Arguments.Any(a => a.IsImplicit))
                {
                    reason = "object creation must bind to a constructor with every argument explicitly supplied.";
                    return false;
                }
            }
            if (node is SimpleNameSyntax name && semantic.GetSymbolInfo(name, token).Symbol is IMethodSymbol &&
                !IsInvokedName(name))
            {
                reason = "method groups and method-name inspection are not supported in the producer body.";
                return false;
            }
        }
        reason = "";
        return true;
    }

    private static bool UnsafeType(ITypeSymbol? type) => type != null &&
        (type.TypeKind is TypeKind.Dynamic or TypeKind.Pointer or TypeKind.FunctionPointer || type.IsRefLikeType);

    private static bool IsInvokedName(SimpleNameSyntax name) =>
        name.Parent is InvocationExpressionSyntax { Expression: var direct } && ReferenceEquals(direct, name) ||
        name.Parent is MemberAccessExpressionSyntax access && ReferenceEquals(access.Name, name) &&
        access.Parent is InvocationExpressionSyntax { Expression: var member } && ReferenceEquals(member, access);

    private static bool ValidateInvocation(SemanticModel semantic, InvocationExpressionSyntax invocation,
        CancellationToken token, out string reason)
    {
        reason = "calls must bind statically to non-generic ordinary methods with every argument explicitly supplied; delegate/dynamic/virtual calls and caller-info defaults are unsupported.";
        if (semantic.GetOperation(invocation, token) is not IInvocationOperation operation ||
            operation.TargetMethod.MethodKind != MethodKind.Ordinary || operation.TargetMethod.IsGenericMethod ||
            operation.TargetMethod.IsVirtual || operation.TargetMethod.IsAbstract || operation.TargetMethod.IsOverride ||
            operation.TargetMethod.ReturnsByRef || operation.TargetMethod.ReturnsByRefReadonly ||
            operation.Arguments.Any(a => a.IsImplicit))
            return false;
        if (operation.TargetMethod.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute"))
        {
            reason = "ConditionalAttribute calls depend on source-file preprocessing and are unsupported.";
            return false;
        }
        if (IsIntrospection(operation.TargetMethod))
        {
            reason = "stack, reflection, and caller inspection are not supported.";
            return false;
        }
        reason = "";
        return true;
    }

    private static bool ValidateAwait(SemanticModel semantic, AwaitExpressionSyntax expression, out string reason)
    {
        var info = semantic.GetAwaitExpressionInfo(expression);
        var owner = info.GetAwaiterMethod?.ContainingType;
        var name = owner?.OriginalDefinition.MetadataName;
        var ns = owner?.ContainingNamespace.ToDisplayString();
        var supported = ns == "System.Threading.Tasks" && name is "Task" or "Task`1" or "ValueTask" or "ValueTask`1" ||
            ns == "System.Runtime.CompilerServices" && name is "YieldAwaitable" or "ConfiguredTaskAwaitable" or
                "ConfiguredTaskAwaitable`1" or "ConfiguredValueTaskAwaitable" or "ConfiguredValueTaskAwaitable`1";
        reason = "only metadata-defined BCL Task, ValueTask, Yield, and configured awaits are supported; custom awaiters have implicit calls that cannot be validated.";
        return supported && !info.IsDynamic && info.GetAwaiterMethod != null && info.IsCompletedProperty != null &&
            info.GetResultMethod != null && info.GetAwaiterMethod.DeclaringSyntaxReferences.Length == 0 &&
            info.IsCompletedProperty.DeclaringSyntaxReferences.Length == 0 && info.GetResultMethod.DeclaringSyntaxReferences.Length == 0 &&
            SymbolEqualityComparer.Default.Equals(owner!.OriginalDefinition,
                semantic.Compilation.GetTypeByMetadataName(ns + "." + name));
    }

    private static bool IsIntrospection(ISymbol symbol)
    {
        var type = (symbol as INamedTypeSymbol ?? symbol.ContainingType)?.ToDisplayString() ?? "";
        return type.StartsWith("System.Reflection.", StringComparison.Ordinal) ||
            type is "System.Diagnostics.StackTrace" or "System.Diagnostics.StackFrame" ||
            type == "System.Environment" && symbol.Name == "StackTrace";
    }

    // Follow source-defined callees and property accessors to reject indirect recursion and
    // delegate/dynamic escape routes. Framework implementations are opaque, as for ordinary C#.
    private static bool ValidateCallGraph(Compilation compilation, IMethodSymbol producer, SyntaxNode body,
        CancellationToken token, out string reason)
    {
        var active = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default) { producer };
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var failure = "";
        bool VisitMethod(IMethodSymbol callee)
        {
            callee = callee.OriginalDefinition;
            if (callee.MethodKind == MethodKind.Constructor && callee.ContainingType.DeclaringSyntaxReferences.Length != 0)
            {
                failure = "source-defined constructors and their initializer call graphs are unsupported.";
                return false;
            }
            if (IsIntrospection(callee))
            {
                failure = "stack, reflection, and caller inspection in source helpers are unsupported.";
                return false;
            }
            if (active.Contains(callee))
            {
                failure = "recursive producer/helper call graphs are unsupported.";
                return false;
            }
            if (callee.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == Marker))
            {
                failure = "calls to another opted-in producer cannot be copied into a generated body.";
                return false;
            }
            if (!visited.Add(callee))
                return true;
            if (visited.Count > 256)
            {
                failure = "the source call graph exceeds the supported 256-method validation limit.";
                return false;
            }
            active.Add(callee);
            foreach (var reference in callee.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax(token);
                SyntaxNode? implementation = syntax switch
                {
                    MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                    ConstructorDeclarationSyntax c => (SyntaxNode?)c.Body ?? c.ExpressionBody,
                    AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
                    PropertyDeclarationSyntax p => (SyntaxNode?)p.ExpressionBody ?? p.AccessorList,
                    IndexerDeclarationSyntax indexer => (SyntaxNode?)indexer.ExpressionBody ?? indexer.AccessorList,
                    _ => syntax
                };
                if (implementation != null && !VisitBody(implementation))
                    return false;
            }
            active.Remove(callee);
            return true;
        }
        bool VisitBody(SyntaxNode implementation)
        {
            var semantic = compilation.GetSemanticModel(implementation.SyntaxTree);
            var rootOperation = semantic.GetOperation(implementation is ArrowExpressionClauseSyntax arrow ? arrow.Expression : implementation, token);
            var operations = new Stack<IOperation>();
            if (rootOperation != null)
                operations.Push(rootOperation);
            while (operations.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                var operation = operations.Pop();
                if (operation is IConversionOperation { OperatorMethod: not null } or
                    IUnaryOperation { OperatorMethod: not null } or IBinaryOperation { OperatorMethod: not null } or
                    IIncrementOrDecrementOperation { OperatorMethod: not null } or ICompoundAssignmentOperation { OperatorMethod: not null })
                {
                    failure = "user-defined operators and conversions have unsupported implicit call graphs.";
                    return false;
                }
                if (operation is IInvocationOperation { IsImplicit: true })
                {
                    failure = "compiler-inserted invocation call graphs are unsupported.";
                    return false;
                }
                if (operation is IObjectCreationOperation { Constructor: { } constructor } && !VisitMethod(constructor))
                    return false;
                if (operation is IPropertyReferenceOperation propertyReference)
                {
                    var property = propertyReference.Property;
                    if (property.IsVirtual || property.IsAbstract || property.IsOverride ||
                        property.GetMethod != null && !VisitMethod(property.GetMethod) ||
                        property.SetMethod != null && !VisitMethod(property.SetMethod))
                    {
                        if (failure.Length == 0)
                            failure = "virtual property dispatch cannot be validated.";
                        return false;
                    }
                }
                if (operation is IEventReferenceOperation or IDynamicInvocationOperation or IDynamicIndexerAccessOperation or
                    IDynamicMemberReferenceOperation or IFunctionPointerInvocationOperation or ITypeParameterObjectCreationOperation)
                {
                    failure = "event, dynamic, function-pointer, and type-parameter construction call graphs are unsupported.";
                    return false;
                }
                foreach (var child in operation.ChildOperations)
                    operations.Push(child);
            }
            foreach (var node in implementation.DescendantNodesAndSelf())
            {
                token.ThrowIfCancellationRequested();
                if (!SupportedHelperSyntax(node))
                {
                    failure = "source helper syntax '" + node.Kind() + "' is outside the supported call-graph subset.";
                    return false;
                }
                if (node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseExpressionSyntax or
                    CollectionExpressionSyntax or InitializerExpressionSyntax or ForEachStatementSyntax or
                    ForEachVariableStatementSyntax or UsingStatementSyntax ||
                    node is LocalDeclarationStatementSyntax local && !local.UsingKeyword.IsKind(SyntaxKind.None))
                {
                    failure = "source helpers with lambdas, local functions, base access, collection/initializer syntax, foreach, or using cannot have their implicit call graphs validated.";
                    return false;
                }
                if (node is AwaitExpressionSyntax awaitExpression && !ValidateAwait(semantic, awaitExpression, out failure))
                    return false;
                if (node is ExpressionSyntax expression &&
                    (semantic.GetConversion(expression, token).IsUserDefined ||
                     semantic.GetOperation(expression, token) is IUnaryOperation { OperatorMethod: not null } or
                         IBinaryOperation { OperatorMethod: not null } or IIncrementOrDecrementOperation { OperatorMethod: not null } or
                         ICompoundAssignmentOperation { OperatorMethod: not null }))
                {
                    failure = "user-defined operators and conversions in source helpers have unsupported implicit call graphs.";
                    return false;
                }
                if (node is IfStatementSyntax conditional && semantic.GetOperation(conditional, token) is IConditionalOperation
                    { Condition: IUnaryOperation { OperatorMethod: not null } })
                {
                    failure = "user-defined truth operators in source helpers cannot be validated.";
                    return false;
                }
                if (node is InvocationExpressionSyntax invocation)
                {
                    if (!ValidateInvocation(semantic, invocation, token, out failure))
                        return false;
                    var called = ((IInvocationOperation)semantic.GetOperation(invocation, token)!).TargetMethod;
                    if (!VisitMethod(called.ReducedFrom ?? called))
                        return false;
                }
                if (node is BaseObjectCreationExpressionSyntax creation &&
                    semantic.GetSymbolInfo(creation, token).Symbol is IMethodSymbol constructor && !VisitMethod(constructor))
                    return false;
                if (node is not SimpleNameSyntax name)
                    continue;
                var symbol = semantic.GetSymbolInfo(name, token).Symbol;
                if (symbol != null && IsIntrospection(symbol))
                {
                    failure = "stack, reflection, and caller inspection in source helpers are unsupported.";
                    return false;
                }
                if (symbol is IMethodSymbol referenced && !IsInvokedName(name))
                {
                    failure = "method groups or method-name inspection in source helpers cannot be validated.";
                    return false;
                }
                if (symbol is IPropertySymbol property)
                {
                    if (property.IsVirtual || property.IsAbstract || property.IsOverride ||
                        property.GetMethod != null && !VisitMethod(property.GetMethod) ||
                        property.SetMethod != null && !VisitMethod(property.SetMethod))
                    {
                        if (failure.Length == 0)
                            failure = "virtual property dispatch in source helpers cannot be validated.";
                        return false;
                    }
                }
            }
            return true;
        }
        var valid = VisitBody(body);
        reason = failure;
        return valid;
    }

    private static bool SupportedHelperSyntax(SyntaxNode node) => node is
        BlockSyntax or TryStatementSyntax or CatchClauseSyntax or FinallyClauseSyntax or
        ExpressionStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax or IfStatementSyntax or
        ElseClauseSyntax or EmptyStatementSyntax or ArrowExpressionClauseSyntax or AccessorListSyntax or AccessorDeclarationSyntax or
        AwaitExpressionSyntax or InvocationExpressionSyntax or MemberAccessExpressionSyntax or IdentifierNameSyntax or
        ThisExpressionSyntax or LiteralExpressionSyntax or ParenthesizedExpressionSyntax or ArgumentListSyntax or ArgumentSyntax or
        NameColonSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or QualifiedNameSyntax or
        AliasQualifiedNameSyntax or PredefinedTypeSyntax or GenericNameSyntax or TypeArgumentListSyntax or NullableTypeSyntax or
        AssignmentExpressionSyntax or BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or
        ConditionalExpressionSyntax or IsPatternExpressionSyntax or ConstantPatternSyntax or UnaryPatternSyntax or
        ElementAccessExpressionSyntax or BracketedArgumentListSyntax or CastExpressionSyntax or CheckedExpressionSyntax or CheckedStatementSyntax or
        DefaultExpressionSyntax or LocalDeclarationStatementSyntax or VariableDeclarationSyntax or VariableDeclaratorSyntax or EqualsValueClauseSyntax;

    private sealed class Site
    {
        internal Site(int version, string data)
        {
            Version = version;
            Data = data;
        }
        internal int Version
        {
            get;
        }
        internal string Data
        {
            get;
        }
    }

    private static bool FindCallSites(Compilation compilation, IMethodSymbol producer, CancellationToken token,
        out List<Site> sites, out string reason, out Location? badLocation)
    {
        sites = new List<Site>();
        reason = "";
        badLocation = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var semantic = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot(token).DescendantNodes().OfType<SimpleNameSyntax>()
                .Where(n => n.Identifier.ValueText == producer.Name))
            {
                token.ThrowIfCancellationRequested();
                var symbol = semantic.GetSymbolInfo(name, token);
                var refers = SymbolEqualityComparer.Default.Equals(symbol.Symbol, producer) ||
                    symbol.CandidateSymbols.Any(s => SymbolEqualityComparer.Default.Equals(s, producer));
                var call = name.Parent as InvocationExpressionSyntax ??
                    (name.Parent is MemberAccessExpressionSyntax access ? access.Parent as InvocationExpressionSyntax : null);
                // Dynamic dispatch has no method symbol. Conservatively reject same-name dynamic calls.
                if (!refers && (call == null || semantic.GetOperation(call, token) is not IDynamicInvocationOperation))
                    continue;
                badLocation = name.GetLocation();
                if (!refers || !IsInvokedName(name) || call == null || call.ArgumentList.Arguments.Count != 0 ||
                    !SymbolEqualityComparer.Default.Equals(semantic.GetSymbolInfo(call, token).Symbol, producer) ||
                    call.Ancestors().Any(a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) ||
                    call.Expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
                {
                    reason = "every producer reference must be a direct, non-dynamic ordinary invocation outside lambdas/local functions; method groups and name inspection are unsupported.";
                    return false;
                }
                if (call.Expression is MemberAccessExpressionSyntax member && member.Expression is not ThisExpressionSyntax)
                {
                    reason = "only implicit-this and explicit-this producer invocations are supported; calls on other receivers are unsupported.";
                    return false;
                }
                var options = (CSharpParseOptions)tree.Options;
                if (!options.Features.TryGetValue("InterceptorsNamespaces", out var namespaces) ||
                    !namespaces.Split(';').Any(ns => ns == InterceptorNamespace || InterceptorNamespace.StartsWith(ns + ".", StringComparison.Ordinal)))
                {
                    reason = "enable the Zendiator.Generated.Interceptors namespace with the InterceptorsNamespaces compiler feature (the Zendiator package supplies it).";
                    return false;
                }
                var location = semantic.GetInterceptableLocation(call, token);
                if (location == null)
                {
                    reason = "a producer invocation has no interceptable source location.";
                    return false;
                }
                sites.Add(new Site(location.Version, location.Data));
            }
        }
        badLocation = null;
        if (sites.Count == 0)
        {
            reason = "no direct interceptable producer invocation was found.";
            return false;
        }
        return true;
    }

    private static string EmitHelper(SemanticModel semantic, MethodDeclarationSyntax declaration, string helperName)
    {
        var b = new StringBuilder("// <auto-generated/>\n");
        var nullable = semantic.GetNullableContext(declaration.SpanStart);
        b.Append("#nullable ").Append((nullable & NullableContext.AnnotationsEnabled) != 0 ? "enable" : "disable").AppendLine(" annotations");
        b.Append("#nullable ").Append((nullable & NullableContext.WarningsEnabled) != 0 ? "enable" : "disable").AppendLine(" warnings");
        void Imports(SyntaxList<ExternAliasDirectiveSyntax> externs, SyntaxList<UsingDirectiveSyntax> usings)
        {
            foreach (var alias in externs)
                b.AppendLine(alias.WithoutTrivia().ToString());
            foreach (var directive in usings.Where(u => !u.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)))
                b.AppendLine(directive.WithoutTrivia().ToString());
        }
        var root = declaration.SyntaxTree.GetCompilationUnitRoot();
        Imports(root.Externs, root.Usings);
        var namespaces = declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().ToArray();
        foreach (var ns in namespaces)
        {
            b.Append("namespace ").Append(ns.Name.WithoutTrivia().ToString()).AppendLine(" {");
            Imports(ns.Externs, ns.Usings);
        }
        var type = (ClassDeclarationSyntax)declaration.Parent!;
        b.Append(semantic.GetDeclaredSymbol(type)!.DeclaredAccessibility == Accessibility.Public ? "public " : "internal ");
        b.Append("partial class ").Append(type.Identifier.Text).AppendLine(" {");
        b.AppendLine("[global::System.Runtime.CompilerServices.AsyncMethodBuilder(typeof(global::System.Runtime.CompilerServices.PoolingAsyncValueTaskMethodBuilder<>))]");
        b.Append("internal async global::System.Threading.Tasks.ValueTask<bool> ").Append(helperName).AppendLine("()");
        b.AppendLine(declaration.Body!.WithoutTrivia().NormalizeWhitespace().ToFullString());
        b.AppendLine("}");
        foreach (var _ in namespaces)
            b.AppendLine("}");
        return b.ToString();
    }

    private static bool ValidateBinding(Compilation augmented, MethodDeclarationSyntax declaration, SyntaxTree helperTree,
        CancellationToken token, out string reason)
    {
        var original = augmented.GetSemanticModel(declaration.SyntaxTree);
        var generated = augmented.GetSemanticModel(helperTree);
        var helper = helperTree.GetRoot(token).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var errors = generated.GetDiagnostics(cancellationToken: token).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            reason = "the helper does not compile in its original scope: " + errors[0].GetMessage();
            return false;
        }
        var left = declaration.Body!.DescendantNodesAndSelf().ToArray();
        var right = helper.Body!.DescendantNodesAndSelf().ToArray();
        if (left.Length != right.Length)
        {
            reason = "the helper body syntax shape changed.";
            return false;
        }
        bool Same(ISymbol? a, ISymbol? b) => SymbolEqualityComparer.Default.Equals(a, b);
        for (var i = 0; i < left.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            reason = "moving '" + left[i].Kind() + "' changed its symbol, conversion, or await binding.";
            if (left[i].RawKind != right[i].RawKind)
                return false;
            if (left[i] is ExpressionSyntax sourceExpression && right[i] is ExpressionSyntax targetExpression)
            {
                var sourceSymbol = original.GetSymbolInfo(sourceExpression, token);
                var targetSymbol = generated.GetSymbolInfo(targetExpression, token);
                var sourceType = original.GetTypeInfo(sourceExpression, token);
                var targetType = generated.GetTypeInfo(targetExpression, token);
                if (sourceSymbol.CandidateReason != CandidateReason.None || targetSymbol.CandidateReason != CandidateReason.None ||
                    !Same(sourceSymbol.Symbol, targetSymbol.Symbol) || !Same(sourceType.Type, targetType.Type) ||
                    !Same(sourceType.ConvertedType, targetType.ConvertedType) ||
                    sourceType.Nullability.FlowState != targetType.Nullability.FlowState ||
                    sourceType.ConvertedNullability.FlowState != targetType.ConvertedNullability.FlowState ||
                    !original.GetConversion(sourceExpression, token).Equals(generated.GetConversion(targetExpression, token)))
                    return false;
                var sourceConstant = original.GetConstantValue(sourceExpression, token);
                var targetConstant = generated.GetConstantValue(targetExpression, token);
                if (sourceConstant.HasValue != targetConstant.HasValue || sourceConstant.HasValue && !Equals(sourceConstant.Value, targetConstant.Value))
                    return false;
            }
            if (left[i] is AwaitExpressionSyntax sourceAwait && right[i] is AwaitExpressionSyntax targetAwait)
            {
                var a = original.GetAwaitExpressionInfo(sourceAwait);
                var b = generated.GetAwaitExpressionInfo(targetAwait);
                if (a.IsDynamic || b.IsDynamic || !Same(a.GetAwaiterMethod, b.GetAwaiterMethod) ||
                    !Same(a.IsCompletedProperty, b.IsCompletedProperty) || !Same(a.GetResultMethod, b.GetResultMethod))
                    return false;
            }
        }
        reason = "";
        return true;
    }

    private static string EmitInterceptors(IMethodSymbol producer, string helperName, string id, IEnumerable<Site> sites)
    {
        var b = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        b.AppendLine("namespace System.Runtime.CompilerServices { [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)] file sealed class InterceptsLocationAttribute : global::System.Attribute { public InterceptsLocationAttribute(int version, string data) {} } }");
        b.Append("namespace ").Append(InterceptorNamespace).Append(" { internal static class PooledProducer_").Append(id).AppendLine(" {");
        foreach (var site in sites)
            b.Append("[global::System.Runtime.CompilerServices.InterceptsLocation(").Append(site.Version).Append(", \"")
                .Append(site.Data.Replace("\\", "\\\\").Replace("\"", "\\\"")).AppendLine("\")]");
        b.Append("internal static global::System.Threading.Tasks.ValueTask<bool> Intercept(this ")
            .Append(producer.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine(" receiver) {");
        // The original instance call throws synchronously on null, before entering its async body.
        b.AppendLine("if (receiver is null) throw new global::System.NullReferenceException();");
        b.Append("return receiver.").Append(helperName).AppendLine("();\n} } }");
        return b.ToString();
    }
}
