using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ChangedHandler<T> : INotificationHandler<Changed<T>>
{
    public static List<string?> Seen { get; } = [];
    public ValueTask HandleAsync(Changed<T> notification, CancellationToken cancellationToken)
    {
        Seen.Add(notification.Value?.ToString());
        return default;
    }
}
