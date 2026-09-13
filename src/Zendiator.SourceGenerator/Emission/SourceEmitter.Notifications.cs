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
            return $$"""
                global::System.Threading.Tasks.ValueTask {{verb}}<{{tp}}>({{route.NotificationDisplay}} notification, global::System.Threading.CancellationToken cancellationToken = default){{string.Concat(route.MethodConstraints)}}
                """;
        }

        return $$"""
            global::System.Threading.Tasks.ValueTask {{verb}}({{route.NotificationDisplay}} notification, global::System.Threading.CancellationToken cancellationToken = default)
            """;
    }

    private static string ErasedSignature(bool publish)
    {
        var verb = publish ? "Publish" : "PublishAsync";
        return $$"""
            global::System.Threading.Tasks.ValueTask {{verb}}(global::Zendiator.INotification notification, global::System.Threading.CancellationToken cancellationToken = default)
            """;
    }

    private static void EmitNotifications(StringBuilder b, List<NotificationRoute> notifications, INamedTypeSymbol notificationHandlerDefinition)
    {
        foreach (var route in notifications)
        {
            b.AppendLine("""    /// <summary>Publishes the notification to subscribers in order.</summary>""");
            if (route.Subscribers.Count == 0)
            {
                b.AppendLine($$"""
                        public {{NotificationSignature(route, publish: false)}}
                        {
                    """);
                if (route.Notification.IsReferenceType)
                    b.AppendLine("""        global::System.ArgumentNullException.ThrowIfNull(notification);""");
                b.AppendLine("""
                            cancellationToken.ThrowIfCancellationRequested();
                            return default;
                        }
                    """);
            }
            else
            {
                b.AppendLine($$"""
                        public async {{NotificationSignature(route, publish: false)}}
                        {
                    """);
                if (route.Notification.IsReferenceType)
                    b.AppendLine("""        global::System.ArgumentNullException.ThrowIfNull(notification);""");
                b.AppendLine("""        cancellationToken.ThrowIfCancellationRequested();""");
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
                        contract = $$"""global::Zendiator.INotificationHandler<{{route.NotificationDisplay}}>""";
                    }

                    var direct = UseDirectCall(sub.Handler, notificationHandlerDefinition);
                    var recv = ServiceReceiver(handlerName, contract, direct, "_services");
                    b.AppendLine($$"""
                                cancellationToken.ThrowIfCancellationRequested();
                                await {{recv}}.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
                        """);
                }

                b.AppendLine("""    }""");
            }

            b.AppendLine($$"""
                    public {{NotificationSignature(route, publish: true)}} => PublishAsync(notification, cancellationToken);
                """);
        }

        var closed = notifications.Where(static n => !n.IsOpen).ToList();
        if (closed.Count != 0)
        {
            b.AppendLine($$"""
                    /// <summary>Publishes a registered notification by its runtime type.</summary>
                    public {{ErasedSignature(publish: false)}}
                    {
                        global::System.ArgumentNullException.ThrowIfNull(notification);
                        cancellationToken.ThrowIfCancellationRequested();
                        var notificationType = notification.GetType();
                """);
            foreach (var route in closed)
                b.AppendLine($$"""
                            if (notificationType == typeof({{route.NotificationDisplay}})) return PublishAsync(({{route.NotificationDisplay}})notification, cancellationToken);
                    """);
            b.AppendLine($$"""
                        throw new global::System.InvalidOperationException($"Unknown notification type '{notificationType}'. Include its assembly or declare it with [assembly: Notification].");
                    }
                    public {{ErasedSignature(publish: true)}} => PublishAsync(notification, cancellationToken);
                """);
        }
    }
}
