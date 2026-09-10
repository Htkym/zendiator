using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ProvisionHandler(AuditLog log) : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"provision:{notification.UserId}");
        return default;
    }
}
