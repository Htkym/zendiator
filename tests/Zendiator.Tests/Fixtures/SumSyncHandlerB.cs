using global::Zendiator;

namespace Zendiator.Tests;

[HandlerOrder(Order = 1)]
public sealed class SumSyncHandlerB : ISyncRequestHandler<SumSync, int>
{
    public int Handle(SumSync request, CancellationToken cancellationToken) => request.A * request.B;
}
