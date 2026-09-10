using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SeenSyncHandler : ISyncRequestHandler<SeenSync>
{
    public static CancellationToken Seen;
    public void Handle(SeenSync request, CancellationToken cancellationToken) => Seen = cancellationToken;
}
