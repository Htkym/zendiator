using global::Zendiator;

namespace Zendiator.Tests;

[HandlerOrder(Order = -1)]
public sealed class MetricsHandler(AuditLog log) : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"metrics:{notification.UserId}");
        return default;
    }
}
