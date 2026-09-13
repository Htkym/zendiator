using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class ChangedFirst : INotificationHandler<Changed>
{
    public static List<int> Seen { get; } = [];
    public ValueTask HandleAsync(Changed notification, CancellationToken cancellationToken)
    {
        Seen.Add(notification.Id);
        ChangedOrder.Events.Add("first:" + notification.Id);
        return default;
    }
}
