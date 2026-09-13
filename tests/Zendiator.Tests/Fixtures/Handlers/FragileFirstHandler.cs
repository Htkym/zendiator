using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FragileFirstHandler : INotificationHandler<Fragile>
{
    public static readonly InvalidOperationException Failure = new("fragile boom");
    public ValueTask HandleAsync(Fragile notification, CancellationToken cancellationToken) => throw Failure;
}
