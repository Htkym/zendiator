using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Zendiator.SourceGenerator;

/// <summary>Warns about direct lifetime mistakes that can be proven within one method.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MediatorLifetimeAnalyzer : DiagnosticAnalyzer
{
    private const string Guidance = "Keep the DI scope alive until dispatch completes; dispose the scope, not the mediator";
    private static readonly DiagnosticDescriptor Disposable = new(
        "ZEN0021", "Generated mediator is not disposable", Guidance,
        "Zendiator", DiagnosticSeverity.Warning, true);
    private static readonly DiagnosticDescriptor EscapesScope = new(
        "ZEN0022", "Mediator escapes its using scope", Guidance,
        "Zendiator", DiagnosticSeverity.Warning, true);
    private static readonly DiagnosticDescriptor SendAfterDispose = new(
        "ZEN0023", "Send follows scope disposal", Guidance,
        "Zendiator", DiagnosticSeverity.Warning, true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Disposable, EscapesScope, SendAfterDispose);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeDisposable,
            SyntaxKind.CastExpression, SyntaxKind.AsExpression, SyntaxKind.IsExpression, SyntaxKind.IsPatternExpression);
        context.RegisterSyntaxNodeAction(AnalyzeReturn, SyntaxKind.ReturnStatement);
        context.RegisterSyntaxNodeAction(AnalyzeSend, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeDisposable(SyntaxNodeAnalysisContext context)
    {
        ExpressionSyntax? expression = null;
        TypeSyntax? target = null;
        switch (context.Node)
        {
            case CastExpressionSyntax cast: expression = cast.Expression; target = cast.Type; break;
            case BinaryExpressionSyntax binary: expression = binary.Left; target = binary.Right as TypeSyntax; break;
            case IsPatternExpressionSyntax pattern:
                var inner = pattern.Pattern;
                while (inner is UnaryPatternSyntax unary) inner = unary.Pattern;
                expression = pattern.Expression;
                target = inner switch
                {
                    DeclarationPatternSyntax declaration => declaration.Type,
                    TypePatternSyntax typePattern => typePattern.Type,
                    ConstantPatternSyntax { Expression: IdentifierNameSyntax name }
                        when context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is INamedTypeSymbol => name,
                    _ => null
                };
                break;
        }
        var disposableType = target is null ? null : context.SemanticModel.GetTypeInfo(target, context.CancellationToken).Type;
        if (expression is null || disposableType is not INamedTypeSymbol { Name: "IDisposable" } namedDisposable ||
            namedDisposable.ContainingNamespace.ToDisplayString() != "System" ||
            !IsMediator(context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type)) return;
        context.ReportDiagnostic(Diagnostic.Create(Disposable, context.Node.GetLocation()));
    }

    private static void AnalyzeReturn(SyntaxNodeAnalysisContext context)
    {
        var statement = (ReturnStatementSyntax)context.Node;
        if (statement.Expression is null || statement.Ancestors().Any(a =>
            a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)) return;
        var expression = Unwrap(statement.Expression);
        ILocalSymbol? scope = null;
        if (expression is IdentifierNameSyntax identifier &&
            context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol is ILocalSymbol mediator &&
            IsMediator(mediator.Type) && TryMediatorScope(mediator, context.SemanticModel, out var localScope) &&
            !WasReassigned(mediator, mediator.DeclaringSyntaxReferences[0].Span.Start, statement.SpanStart,
                statement.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault() ?? (SyntaxNode)statement,
                context.SemanticModel))
            scope = localScope;
        else if (TryResolutionScope(expression, context.SemanticModel, out var directScope))
            scope = directScope;

        if (scope is null || !UsingScopeContains(scope, statement)) return;
        context.ReportDiagnostic(Diagnostic.Create(EscapesScope, statement.GetLocation()));
    }

    private static void AnalyzeSend(SyntaxNodeAnalysisContext context)
    {
        if (context.Node is not InvocationExpressionSyntax send ||
            send.Ancestors().Any(a => a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) ||
            send.Expression is not MemberAccessExpressionSyntax { Name.Identifier.Text: "SendAsync", Expression: IdentifierNameSyntax receiver } ||
            context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol is not ILocalSymbol mediator ||
            !IsMediator(mediator.Type) || !TryMediatorScope(mediator, context.SemanticModel, out var scope) ||
            send.Ancestors().OfType<StatementSyntax>().FirstOrDefault(s => s.Parent is BlockSyntax) is not { Parent: BlockSyntax block } sendStatement)
            return;

        foreach (var statement in block.Statements)
        {
            if (statement.SpanStart >= sendStatement.SpanStart) break;
            if (statement is not ExpressionStatementSyntax expression ||
                !IsDisposeCall(expression.Expression, scope, context.SemanticModel)) continue;
            if (WasReassigned(mediator, statement.Span.End, send.SpanStart, block, context.SemanticModel) ||
                WasReassigned(scope, statement.Span.End, send.SpanStart, block, context.SemanticModel)) continue;
            context.ReportDiagnostic(Diagnostic.Create(SendAfterDispose, send.GetLocation()));
            return;
        }
    }

    private static bool IsDisposeCall(ExpressionSyntax expression, ILocalSymbol scope, SemanticModel model)
    {
        var awaitedDispose = expression is AwaitExpressionSyntax;
        if (expression is AwaitExpressionSyntax awaited) expression = awaited.Expression;
        return expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } access
        } && access.Name.Identifier.Text is "Dispose" or "DisposeAsync" &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(receiver).Symbol, scope) &&
            model.GetSymbolInfo(expression).Symbol is IMethodSymbol method &&
            method.Parameters.Length == 0 && (method.Name == "Dispose" || awaitedDispose);
    }

    private static bool TryMediatorScope(ILocalSymbol mediator, SemanticModel model, out ILocalSymbol scope)
    {
        scope = null!;
        return mediator.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax
        { Initializer.Value: ExpressionSyntax initializer } && TryResolutionScope(initializer, model, out scope);
    }

    private static bool TryResolutionScope(ExpressionSyntax expression, SemanticModel model, out ILocalSymbol scope)
    {
        scope = null!;
        expression = Unwrap(expression);
        if (expression is not InvocationExpressionSyntax
            { Expression: MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax provider } } invocation ||
            provider.Name.Identifier.Text != "ServiceProvider" ||
            provider.Expression is not IdentifierNameSyntax scopeName ||
            model.GetSymbolInfo(scopeName).Symbol is not ILocalSymbol local ||
            model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            method.Name is not ("GetRequiredService" or "GetService") ||
            !IsMediator(model.GetTypeInfo(invocation).Type) || !IsScope(local)) return false;
        scope = local;
        return true;
    }

    private static bool IsScope(ILocalSymbol local) =>
        local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax
        { Initializer.Value: InvocationExpressionSyntax invocation } &&
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "CreateScope" or "CreateAsyncScope" } &&
        local.Type is INamedTypeSymbol scopeType &&
        scopeType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" &&
        scopeType.Name is "IServiceScope" or "AsyncServiceScope";

    private static bool UsingScopeContains(ILocalSymbol scope, ReturnStatementSyntax statement)
    {
        if (scope.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not VariableDeclaratorSyntax declarator) return false;
        if (declarator.Parent?.Parent is LocalDeclarationStatementSyntax declaration &&
            declaration.UsingKeyword != default && declaration.Parent is BlockSyntax block &&
            declaration.SpanStart < statement.SpanStart && block.Span.Contains(statement.Span)) return true;
        return declarator.Parent?.Parent is UsingStatementSyntax usingStatement &&
            usingStatement.Statement.Span.Contains(statement.Span);
    }

    private static bool WasReassigned(ISymbol symbol, int start, int end, SyntaxNode container, SemanticModel model) =>
        container.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(a =>
            a.SpanStart > start && a.SpanStart < end &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(a.Left).Symbol, symbol));

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized: expression = parenthesized.Expression; break;
                case CastExpressionSyntax cast: expression = cast.Expression; break;
                default: return expression;
            }
        }
    }

    private static bool IsMediator(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named) return false;
        if (named.Name is not ("Zendiator" or "IZendiator")) return false;
        return named.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.CodeDom.Compiler.GeneratedCodeAttribute" &&
            attribute.ConstructorArguments.Length == 2 &&
            attribute.ConstructorArguments[0].Value as string == "Zendiator.SourceGenerator");
    }
}
