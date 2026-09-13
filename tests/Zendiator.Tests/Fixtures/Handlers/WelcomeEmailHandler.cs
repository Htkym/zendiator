using global::Zendiator;

namespace Zendiator.Tests;

[HandlerOrder(Order = 1)]
public sealed class WelcomeEmailHandler(AuditLog log) : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"mail:{notification.UserId}");
        return default;
    }
}
