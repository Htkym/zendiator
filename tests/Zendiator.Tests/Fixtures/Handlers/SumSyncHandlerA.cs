using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SumSyncHandlerA : ISyncRequestHandler<SumSync, int>
{
    public int Handle(SumSync request, CancellationToken cancellationToken) => request.A + request.B;
}
