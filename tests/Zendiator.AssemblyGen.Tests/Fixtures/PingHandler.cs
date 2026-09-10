using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

public sealed class PingHandler : IRequestHandler<Ping, int>
{
    public ValueTask<int> HandleAsync(Ping request, CancellationToken cancellationToken) => new(request.Value * 2);
}
