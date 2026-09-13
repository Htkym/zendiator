using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZEv4HandlerB : INotificationHandler<ZEv4>
{
    public ValueTask HandleAsync(ZEv4 notification, CancellationToken cancellationToken) => default;
}
