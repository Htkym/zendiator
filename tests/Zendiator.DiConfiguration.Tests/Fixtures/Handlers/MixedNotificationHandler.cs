namespace Zendiator.DiConfiguration.Tests;

// Notifications retain the legacy registration backend, separately from marked native routes.
public sealed class MixedNotificationHandler : INotificationHandler<MixedNoteFirst>, INotificationHandler<MixedNoteSecond>
{
    public int NotificationResult { get; private set; }
    public ValueTask HandleAsync(MixedNoteFirst notification, CancellationToken cancellationToken) => default;
    ValueTask INotificationHandler<MixedNoteSecond>.HandleAsync(MixedNoteSecond notification, CancellationToken cancellationToken)
    {
        NotificationResult = 7;
        return default;
    }
    public ValueTask HandleAsync(MixedNoteSecond notification, CancellationToken cancellationToken)
    {
        NotificationResult = 99;
        return default;
    }
}
