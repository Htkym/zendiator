using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private static string NotificationSignature(EmissionNotificationRoute route, bool publish)
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

    private void EmitNotifications(StringBuilder b)
    {
        for (var index = 0; index < _model.Routes.Notifications.Count; index++)
            EmitNotification(b, _model.Routes.Notifications[index], index);
        var closed = _model.Routes.Notifications.Where(static n => !n.IsOpen).ToList();
        if (closed.Count != 0)
        {
            b.AppendLine($$"""
                    /// <summary>Publishes a registered notification by its runtime type.</summary>
                    public {{ErasedSignature(publish: false)}}
                    {
                        global::System.ArgumentNullException.ThrowIfNull(notification);
                        if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);
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

    private void EmitNotification(StringBuilder b, EmissionNotificationRoute route, int index)
    {
        var set = SetOf("NotificationRoute" + index);
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
                        if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);
                        return default;
                    }
                """);
        }
        else if (route.Subscribers.Count == 1 && !route.IsOpen)
        {
            EmitSingleSubscriberNotification(b, route, index);
        }
        else
        {
            b.AppendLine($$"""
                    public async {{NotificationSignature(route, publish: false)}}
                    {
                """);
            if (route.Notification.IsReferenceType)
                b.AppendLine("""        global::System.ArgumentNullException.ThrowIfNull(notification);""");
            b.AppendLine("""        if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);""");
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

                var direct = sub.Handler.DirectCall;
                var recv = Receiver(set, sub.Handler, handlerName, contract, direct, MediatorServices,
                    set == null ? "" : set.Name + ".Get(" + MediatorServices + ")");
                if (i != 0)
                    b.AppendLine("""        if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);""");
                b.AppendLine($$"""
                            await {{recv}}.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
                    """);
            }

            b.AppendLine("""    }""");
        }

        b.AppendLine($$"""
                public {{NotificationSignature(route, publish: true)}} => PublishAsync(notification, cancellationToken);
            """);
    }

    // Start restores the caller's contexts like an async method without boxing a state machine.
    private void EmitSingleSubscriberNotification(StringBuilder b, EmissionNotificationRoute route, int index)
    {
        var handler = route.Subscribers[0].Handler;
        var contract = $$"""global::Zendiator.INotificationHandler<{{route.NotificationDisplay}}>""";
        var publish = $"NotificationRoute{index}Publish";
        b.AppendLine($$"""
                public {{NotificationSignature(route, publish: false)}}
                {
                    var publish = new {{publish}}({{MediatorServices}}, notification, cancellationToken);
                    global::System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder.Create().Start(ref publish);
                    return publish.Result;
                }
                private struct {{publish}}({{ResolverType}} services, {{route.NotificationDisplay}} notification, global::System.Threading.CancellationToken cancellationToken) : global::System.Runtime.CompilerServices.IAsyncStateMachine
                {
                    internal global::System.Threading.Tasks.ValueTask Result;
                    public void MoveNext()
                    {
                        try
                        {
            """);
        if (route.Notification.IsReferenceType)
            b.AppendLine("""                global::System.ArgumentNullException.ThrowIfNull(notification);""");
        b.AppendLine($$"""
                            if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);
                            var operation = {{ServiceReceiver(Name(handler), contract, handler.DirectCall)}}.HandleAsync(notification, cancellationToken);
                            if (operation.IsCompletedSuccessfully)
                            {
                                operation.GetAwaiter().GetResult();
                                Result = default;
                            }
                            else
                            {
                                Result = AwaitSingleSubscriberNotification(operation);
                            }
                        }
                        catch (global::System.Exception exception)
                        {
                            Result = Faulted(global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception));
                        }
                    }
                    public void SetStateMachine(global::System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) { }
                }
            """);
    }
}
