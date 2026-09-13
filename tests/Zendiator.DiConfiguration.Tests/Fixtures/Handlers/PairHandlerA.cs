using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class PairHandlerA : IRequestHandler<Pair, int>
{
    public ValueTask<int> HandleAsync(Pair request, CancellationToken cancellationToken) => new(request.Value + 1);
}
