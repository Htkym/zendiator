using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ExplicitSyncHandler : ISyncRequestHandler<ExplicitSync, int>
{
    int ISyncRequestHandler<ExplicitSync, int>.Handle(ExplicitSync request, CancellationToken cancellationToken) => 7;
}
