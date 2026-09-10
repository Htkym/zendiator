using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiBench;

public sealed class DuoHandlerB : IRequestHandler<Duo, int>
{
    public ValueTask<int> HandleAsync(Duo request, CancellationToken cancellationToken) => new(request.Value + 2);
}
