using Zendiator;

namespace Zendiator.Allocation.Tests;

/// <summary>Synchronously completed handler with no allocations.</summary>
public sealed class PingHandler : IRequestHandler<Ping, int>
{
    public ValueTask<int> HandleAsync(Ping request, CancellationToken cancellationToken) => new(request.Value);
}
