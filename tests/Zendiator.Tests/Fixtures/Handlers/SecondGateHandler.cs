using global::Zendiator;

namespace Zendiator.Tests;

[HandlerOrder(Order = 1)]
public sealed class SecondGateHandler(Gate gate) : INotificationHandler<SyncPoint>
{
    public ValueTask HandleAsync(SyncPoint notification, CancellationToken cancellationToken)
    {
        gate.Events.Add("second");
        return default;
    }
}
