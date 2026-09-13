using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class AddHandler : IRequestHandler<Add, int>
{
    public ValueTask<int> HandleAsync(Add request, CancellationToken cancellationToken) =>
        new(request.Left + request.Right);
}
