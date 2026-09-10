using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.GeneratedNamespace;

namespace Zendiator.SourceGenerator;

internal sealed partial class GenerationAnalysis
{
    private DiSetting? AnalyzeDiLambda(InvocationExpressionSyntax invocation, LambdaExpressionSyntax lambda, SemanticModel model)
    {
        if (lambda.AsyncKeyword != default)
        {
            ErrorAt(17, "AddZendiator configuration must be a synchronous lambda. Remove async.", lambda.GetLocation());
            return null;
        }
        string? parameterName = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList?.Parameters.Count == 1 => parenthesized.ParameterList.Parameters[0].Identifier.Text,
            _ => null,
        };
        if (parameterName == null)
        {
            ErrorAt(17, "AddZendiator configuration lambda must take exactly one parameter.", lambda.GetLocation());
            return null;
        }
        var setting = new DiSetting { CallLocation = invocation.GetLocation() };
        var valid = true;
        if (lambda.Body is BlockSyntax block)
        {
            foreach (var statement in block.Statements)
            {
                ct.ThrowIfCancellationRequested();
                if (!AnalyzeDiStatement(statement, parameterName, model, setting)) valid = false;
            }
        }
        else if (lambda.Body is ExpressionSyntax expression)
        {
            if (expression is InvocationExpressionSyntax single &&
                single.Expression is MemberAccessExpressionSyntax singleAccess &&
                singleAccess.Expression is IdentifierNameSyntax singleTarget &&
                singleTarget.Identifier.Text == parameterName)
            {
                if (!AnalyzeDiCall(single, singleAccess.Name.Identifier.Text, model, setting)) valid = false;
            }
            else if (expression is AssignmentExpressionSyntax assignment &&
                assignment.Left is MemberAccessExpressionSyntax assignAccess &&
                assignAccess.Expression is IdentifierNameSyntax assignTarget &&
                assignTarget.Identifier.Text == parameterName)
            {
                if (!AnalyzeDiAssignment(assignAccess.Name.Identifier.Text, assignment.Right, model, setting)) valid = false;
            }
            else
            {
                ErrorAt(17, "AddZendiator configuration lambda must be a block of recorder calls or a single recorder call or assignment.", lambda.Body.GetLocation());
                return null;
            }
        }
        else
        {
            ErrorAt(17, "AddZendiator configuration lambda must be a block of recorder calls or a single recorder call.", lambda.Body.GetLocation());
            return null;
        }
        return valid ? setting : null;
    }

    private bool AnalyzeDiStatement(StatementSyntax statement, string parameterName, SemanticModel model, DiSetting setting)
    {
        if (statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } &&
            invocation.Expression is MemberAccessExpressionSyntax access &&
            access.Expression is IdentifierNameSyntax target &&
            target.Identifier.Text == parameterName)
        {
            return AnalyzeDiCall(invocation, access.Name.Identifier.Text, model, setting);
        }
        if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
            assignment.Left is MemberAccessExpressionSyntax left &&
            left.Expression is IdentifierNameSyntax leftTarget &&
            leftTarget.Identifier.Text == parameterName)
        {
            return AnalyzeDiAssignment(left.Name.Identifier.Text, assignment.Right, model, setting);
        }
        ErrorAt(17, "Unsupported AddZendiator configuration statement. Use direct recorder calls (RegisterServicesFromAssemblyContaining, AddOpenBehavior, AddOpenStreamBehavior, AddNotification, ConfigureHandlerOrder) and property assignments (Namespace, ServiceLifetime).", statement.GetLocation());
        return false;
    }

    private bool AnalyzeDiCall(InvocationExpressionSyntax invocation, string methodName, SemanticModel model, DiSetting setting)
    {
        var location = invocation.GetLocation();
        INamedTypeSymbol? TypeArgument(int index)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax access) return null;
            if (access.Name is not GenericNameSyntax generic) return null;
            if (generic.TypeArgumentList.Arguments.Count <= index) return null;
            return model.GetSymbolInfo(generic.TypeArgumentList.Arguments[index], ct).Symbol as INamedTypeSymbol;
        }
        int? IntArgument(int index)
        {
            if (invocation.ArgumentList == null || invocation.ArgumentList.Arguments.Count <= index) return null;
            var constant = model.GetConstantValue(invocation.ArgumentList.Arguments[index].Expression, ct);
            return constant.HasValue && constant.Value is int value ? value : (int?)null;
        }
        INamedTypeSymbol? TypeofArgument(int index, out bool supported)
        {
            supported = true;
            if (invocation.ArgumentList == null || invocation.ArgumentList.Arguments.Count <= index)
            {
                supported = false;
                return null;
            }
            var expression = invocation.ArgumentList.Arguments[index].Expression;
            if (expression is TypeOfExpressionSyntax { Type: { } type })
                return model.GetSymbolInfo(type, ct).Symbol as INamedTypeSymbol;
            supported = false;
            return null;
        }
        switch (methodName)
        {
            case "RegisterServicesFromAssemblyContaining":
                {
                    if (invocation.ArgumentList?.Arguments.Count != 0) goto Unsupported;
                    var marker = TypeArgument(0);
                    if (marker != null && marker.IsUnboundGenericType) marker = marker.OriginalDefinition;
                    var key = marker == null ? null : FullNameOf(marker);
                    if (key == null) goto Unsupported;
                    if (!setting.Markers.Any(entry => entry.Key == key))
                        setting.Markers.Add((marker!, key));
                    return true;
                }
            case "RegisterServicesFromAssembly":
                {
                    if (invocation.ArgumentList?.Arguments.Count != 1) goto Unsupported;
                    var expression = invocation.ArgumentList.Arguments[0].Expression;
                    if (expression is MemberAccessExpressionSyntax
                        {
                            Name.Identifier.Text: "Assembly",
                            Expression: TypeOfExpressionSyntax { Type: { } type }
                        } &&
                        model.GetSymbolInfo(type, ct).Symbol is INamedTypeSymbol target &&
                        target.ContainingAssembly != null)
                    {
                        var identity = target.ContainingAssembly.Identity.ToString();
                        if (!setting.Assemblies.Contains(identity)) setting.Assemblies.Add(identity);
                        return true;
                    }
                    goto Unsupported;
                }
            case "AddOpenBehavior":
            case "AddOpenStreamBehavior":
                {
                    if (invocation.ArgumentList?.Arguments.Count != 2) goto Unsupported;
                    var behavior = TypeofArgument(0, out var supported);
                    var order = IntArgument(1);
                    if (!supported || behavior == null || order == null) goto Unsupported;
                    var key = FullNameOf(behavior);
                    if (key == null) goto Unsupported;
                    var isOpen = behavior.IsUnboundGenericType;
                    if (setting.Behaviors.Any(entry => entry.Key == key))
                    {
                        ErrorAt(18, $"Behavior {key} is configured more than once. Register each behavior type once.", location);
                        return false;
                    }
                    if (setting.Behaviors.Any(entry => entry.Order == order))
                    {
                        ErrorAt(18, $"Behavior order {order} is used more than once. Order values must be unique.", location);
                        return false;
                    }
                    setting.Behaviors.Add((isOpen ? behavior.OriginalDefinition : behavior, isOpen, order.Value, key));
                    return true;
                }
            case "AddNotification":
                {
                    if (invocation.ArgumentList?.Arguments.Count != 0) goto Unsupported;
                    var notification = TypeArgument(0);
                    if (notification != null && notification.IsUnboundGenericType) notification = notification.OriginalDefinition;
                    var key = notification == null ? null : FullNameOf(notification);
                    if (key == null) goto Unsupported;
                    if (setting.Notifications.Any(entry => entry.Key == key))
                    {
                        ErrorAt(18, $"Notification {key} is declared more than once.", location);
                        return false;
                    }
                    setting.Notifications.Add((notification!, key));
                    return true;
                }
            case "ConfigureHandlerOrder":
                {
                    if (invocation.ArgumentList?.Arguments.Count != 2) goto Unsupported;
                    var handler = TypeofArgument(0, out var supported);
                    var order = IntArgument(1);
                    if (!supported || handler == null || order == null) goto Unsupported;
                    if (handler.IsUnboundGenericType) handler = handler.OriginalDefinition;
                    var key = FullNameOf(handler);
                    if (key == null) goto Unsupported;
                    if (setting.HandlerOrders.Any(entry => entry.Key == key))
                    {
                        ErrorAt(18, $"Handler {key} has more than one configured order.", location);
                        return false;
                    }
                    setting.HandlerOrders.Add((handler, key, order.Value));
                    return true;
                }
            default:
                ErrorAt(17, $"Unsupported AddZendiator configuration call '{methodName}'. Supported calls: RegisterServicesFromAssemblyContaining, RegisterServicesFromAssembly, AddOpenBehavior, AddOpenStreamBehavior, AddNotification, ConfigureHandlerOrder.", location);
                return false;
        }
    Unsupported:
        ErrorAt(17, $"Unsupported AddZendiator configuration shape for '{methodName}'. Use typeof arguments, generic type arguments, and constant values as documented.", location);
        return false;
    }

    private bool AnalyzeDiAssignment(string propertyName, ExpressionSyntax value, SemanticModel model, DiSetting setting)
    {
        var location = value.GetLocation();
        switch (propertyName)
        {
            case "Namespace":
                {
                    var constant = model.GetConstantValue(value, ct);
                    if (!constant.HasValue || constant.Value == null)
                    {
                        if (constant.HasValue)
                        {
                            setting.Namespace = null;
                            setting.NamespaceLocation = location;
                            return true;
                        }
                        ErrorAt(17, "Namespace must be a string constant. Assign a literal, const, or null.", location);
                        return false;
                    }
                    if (constant.Value is not string text)
                    {
                        ErrorAt(18, "Namespace must be a string.", location);
                        return false;
                    }
                    if (setting.NamespaceLocation != null)
                    {
                        ErrorAt(18, "Namespace is assigned more than once. Assign it once.", location);
                        return false;
                    }
                    if (!IsValidNamespace(text))
                    {
                        ErrorAt(18, $"Invalid generation namespace '{text}'. Use dot-separated identifiers.", location);
                        return false;
                    }
                    setting.Namespace = text;
                    setting.NamespaceLocation = location;
                    return true;
                }
            case "ServiceLifetime":
                {
                    // Lifetime flows at runtime and never shapes generation; only the
                    // value domain is checked here.
                    var constant = model.GetConstantValue(value, ct);
                    if (constant.HasValue && constant.Value is int lifetime &&
                        lifetime is not (0 or 1 or 2))
                    {
                        ErrorAt(18, "ServiceLifetime must be Transient, Scoped, or Singleton.", location);
                        return false;
                    }
                    return true;
                }
            default:
                ErrorAt(17, $"Unsupported AddZendiator configuration property '{propertyName}'. Supported properties: Namespace, ServiceLifetime.", location);
                return false;
        }
    }
}
