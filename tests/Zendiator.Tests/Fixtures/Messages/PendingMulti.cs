using global::Zendiator;

namespace Zendiator.Tests;

public sealed record PendingMulti(Task<int> First, Task<int> Second) : IMultiRequest<int>
{
    public List<int> Started { get; } = [];
}

[HandlerOrder(Order = 0)]
public sealed class PendingMultiFirst : IRequestHandler<PendingMulti, int>
{
    public ValueTask<int> HandleAsync(PendingMulti request, CancellationToken cancellationToken)
    {
        request.Started.Add(1);
        return new(request.First);
    }
}

[HandlerOrder(Order = 1)]
public sealed class PendingMultiSecond : IRequestHandler<PendingMulti, int>
{
    public ValueTask<int> HandleAsync(PendingMulti request, CancellationToken cancellationToken)
    {
        request.Started.Add(2);
        return new(request.Second);
    }
}
