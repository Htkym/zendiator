using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FragileSecondHandler : INotificationHandler<Fragile>
{
    public static int Calls;
    public ValueTask HandleAsync(Fragile notification, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return default;
    }
}
