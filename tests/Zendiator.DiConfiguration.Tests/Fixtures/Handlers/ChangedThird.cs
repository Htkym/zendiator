using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

[HandlerOrder(Order = 2)]
public sealed class ChangedThird : INotificationHandler<Changed>
{
    public static List<int> Seen { get; } = [];
    public ValueTask HandleAsync(Changed notification, CancellationToken cancellationToken)
    {
        Seen.Add(notification.Id * 100);
        ChangedOrder.Events.Add("third:" + notification.Id);
        return default;
    }
}
