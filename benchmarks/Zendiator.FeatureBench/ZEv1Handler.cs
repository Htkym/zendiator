using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZEv1Handler : INotificationHandler<ZEv1>
{
    public ValueTask HandleAsync(ZEv1 notification, CancellationToken cancellationToken) => default;
}
