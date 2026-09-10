using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FirstGateHandler(Gate gate) : INotificationHandler<SyncPoint>
{
    public async ValueTask HandleAsync(SyncPoint notification, CancellationToken cancellationToken)
    {
        gate.Events.Add("first-start");
        gate.FirstEntered.TrySetResult();
        await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        gate.Events.Add("first-end");
    }
}
