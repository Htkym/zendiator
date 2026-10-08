namespace Zendiator.Tests;

public sealed record SharedLeadingNote(NotificationCompletion Completion, short Version) : INotification;
public sealed record SharedLeadingOtherNote : INotification;

[HandlerOrder(Order = -1)]
public sealed class SharedLeadingSubscriber : INotificationHandler<SharedLeadingNote>, INotificationHandler<SharedLeadingOtherNote>
{
    public ValueTask HandleAsync(SharedLeadingNote notification, CancellationToken cancellationToken)
        => new(notification.Completion, notification.Version);
    public ValueTask HandleAsync(SharedLeadingOtherNote notification, CancellationToken cancellationToken) => default;
}

public sealed class SharedLeadingLaterA : INotificationHandler<SharedLeadingNote>
{
    public ValueTask HandleAsync(SharedLeadingNote notification, CancellationToken cancellationToken) => default;
}

[HandlerOrder(Order = 1)]
public sealed class SharedLeadingLaterB : INotificationHandler<SharedLeadingNote>
{
    public ValueTask HandleAsync(SharedLeadingNote notification, CancellationToken cancellationToken) => default;
}
