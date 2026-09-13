using global::Zendiator;

namespace Zendiator.Tests;

public sealed class TickHandler : INotificationHandler<Tick>
{
    public static int Sum;
    public ValueTask HandleAsync(Tick notification, CancellationToken cancellationToken)
    {
        Interlocked.Add(ref Sum, notification.N);
        return default;
    }
}
