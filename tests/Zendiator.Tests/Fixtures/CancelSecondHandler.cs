using global::Zendiator;

namespace Zendiator.Tests;

public sealed class CancelSecondHandler : INotificationHandler<CancelMid>
{
    public static int Calls;
    public ValueTask HandleAsync(CancelMid notification, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return default;
    }
}
