using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

[HandlerOrder(Order = 0)]
public sealed class ChangedSecond : INotificationHandler<Changed>
{
    public static List<int> Seen { get; } = [];
    public ValueTask HandleAsync(Changed notification, CancellationToken cancellationToken)
    {
        Seen.Add(notification.Id * 10);
        ChangedOrder.Events.Add("second:" + notification.Id);
        return default;
    }
}
