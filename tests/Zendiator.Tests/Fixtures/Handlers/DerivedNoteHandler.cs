using global::Zendiator;

namespace Zendiator.Tests;

public sealed class DerivedNoteHandler(AuditLog log) : INotificationHandler<DerivedNote>
{
    public ValueTask HandleAsync(DerivedNote notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"derived:{notification.Id}:{notification.Extra}");
        return default;
    }
}
