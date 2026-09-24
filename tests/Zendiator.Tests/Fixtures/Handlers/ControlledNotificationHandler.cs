namespace Zendiator.Tests;

public sealed class ControlledNotificationHandler : INotificationHandler<ControlledNotification>
{
    public ValueTask HandleAsync(ControlledNotification notification, CancellationToken cancellationToken)
    {
        notification.Context.Value = "subscriber";
        return new ValueTask(notification.Source, notification.Version);
    }
}
