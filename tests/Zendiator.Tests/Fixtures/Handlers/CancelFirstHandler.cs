using global::Zendiator;

namespace Zendiator.Tests;

public sealed class CancelFirstHandler : INotificationHandler<CancelMid>
{
    public static CancellationTokenSource? Live;
    public ValueTask HandleAsync(CancelMid notification, CancellationToken cancellationToken)
    {
        Live?.Cancel();
        return default;
    }
}
