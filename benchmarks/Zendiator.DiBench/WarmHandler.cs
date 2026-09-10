using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiBench;

public sealed class WarmHandler : IRequestHandler<Warm, int>
{
    public ValueTask<int> HandleAsync(Warm request, CancellationToken cancellationToken) => new(request.Value + 1);
}
