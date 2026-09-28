using Zendiator;

namespace Zendiator.Allocation.Tests;

public readonly record struct SyncMulti(int Value) : ISyncMultiRequest<int>;

[HandlerOrder(Order = 0)]
public sealed class SyncMultiFirst : ISyncRequestHandler<SyncMulti, int>
{
    public int Handle(SyncMulti request, CancellationToken cancellationToken) => request.Value;
}

[HandlerOrder(Order = 1)]
public sealed class SyncMultiSecond : ISyncRequestHandler<SyncMulti, int>
{
    public int Handle(SyncMulti request, CancellationToken cancellationToken) => request.Value + 1;
}
