using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

[HandlerOrder(Order = 1)]
public sealed class PairHandlerB : IRequestHandler<Pair, int>
{
    public ValueTask<int> HandleAsync(Pair request, CancellationToken cancellationToken) => new(request.Value * 10);
}
