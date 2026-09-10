using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiBench;

public sealed class WipeHandler : IRequestHandler<Wipe>
{
    public ValueTask HandleAsync(Wipe request, CancellationToken cancellationToken) => default;
}
