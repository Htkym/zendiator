namespace Zendiator;

/// <summary>Handles one notification. There is no response and no automatic retry.</summary>
public interface INotificationHandler<TNotification>
    where TNotification : INotification
{
    /// <summary>Handles the notification. Await the returned value only once.</summary>
    ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken);
}
