using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static string NotificationSignature(NotificationRoute route, bool publish)
    {
        var verb = publish ? "Publish" : "PublishAsync";
        if (route.IsOpen)
        {
            var tp = string.Join(", ", route.OpenTypeParams);
            return $"global::System.Threading.Tasks.ValueTask {verb}<{tp}>({route.NotificationDisplay} notification, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
        }
        return $"global::System.Threading.Tasks.ValueTask {verb}({route.NotificationDisplay} notification, global::System.Threading.CancellationToken cancellationToken = default)";
    }

    private static string ErasedSignature(bool publish)
    {
        var verb = publish ? "Publish" : "PublishAsync";
        return $"global::System.Threading.Tasks.ValueTask {verb}(global::Zendiator.INotification notification, global::System.Threading.CancellationToken cancellationToken = default)";
    }

    private static void EmitNotifications(StringBuilder b, List<NotificationRoute> notifications, INamedTypeSymbol notificationHandlerDefinition)
    {
        foreach (var route in notifications)
        {
            b.AppendLine("    /// <summary>Publishes the notification to subscribers in order.</summary>");
            if (route.Subscribers.Count == 0)
            {
                b.Append("    public ").Append(NotificationSignature(route, publish: false)).AppendLine("\n    {");
                if (route.Notification.IsReferenceType) b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(notification);");
                b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
                b.AppendLine("        return default;\n    }");
            }
            else
            {
                b.Append("    public async ").Append(NotificationSignature(route, publish: false)).AppendLine("\n    {");
                if (route.Notification.IsReferenceType) b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(notification);");
                b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
                for (var i = 0; i < route.Subscribers.Count; i++)
                {
                    var sub = route.Subscribers[i];
                    string handlerName, contract;
                    if (route.IsOpen)
                    {
                        handlerName = route.HandlerDisplays[i];
                        contract = route.HandlerContractDisplays[i];
                    }
                    else
                    {
                        handlerName = Name(sub.Handler);
                        contract = $"global::Zendiator.INotificationHandler<{route.NotificationDisplay}>";
                    }
                    var direct = UseDirectCall(sub.Handler, notificationHandlerDefinition);
                    var recv = direct
                        ? $"_services.GetRequiredService<{handlerName}>()"
                        : "((" + contract + $")_services.GetRequiredService<{handlerName}>())";
                    b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
                    b.Append("        await ").Append(recv).AppendLine(".HandleAsync(notification, cancellationToken).ConfigureAwait(false);");
                }
                b.AppendLine("    }");
            }
            b.Append("    public ").Append(NotificationSignature(route, publish: true)).Append(" => PublishAsync(notification, cancellationToken);\n");
        }
        var closed = notifications.Where(static n => !n.IsOpen).ToList();
        if (closed.Count != 0)
        {
            b.AppendLine("    /// <summary>Publishes a registered notification by its runtime type.</summary>");
            b.Append("    public ").Append(ErasedSignature(publish: false)).AppendLine("\n    {");
            b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(notification);");
            b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            b.AppendLine("        var notificationType = notification.GetType();");
            foreach (var route in closed)
                b.Append("        if (notificationType == typeof(").Append(route.NotificationDisplay).Append(")) return PublishAsync((").Append(route.NotificationDisplay).AppendLine(")notification, cancellationToken);");
            b.AppendLine("        throw new global::System.InvalidOperationException($\"Unknown notification type '{notificationType}'. Include its assembly or declare it with [assembly: Notification].\");\n    }");
            b.Append("    public ").Append(ErasedSignature(publish: true)).Append(" => PublishAsync(notification, cancellationToken);\n");
        }
    }
}
