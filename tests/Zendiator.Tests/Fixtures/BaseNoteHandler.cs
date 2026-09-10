using global::Zendiator;

namespace Zendiator.Tests;

public sealed class BaseNoteHandler(AuditLog log) : INotificationHandler<BaseNote>
{
    public ValueTask HandleAsync(BaseNote notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"base:{notification.Id}");
        return default;
    }
}
