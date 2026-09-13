using global::Zendiator;

namespace Zendiator.Tests;

[HandlerOrder(Order = 0)]
public sealed class AlphaHandler(AuditLog log) : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Events.Add("alpha");
        return default;
    }
}
