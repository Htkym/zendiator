using global::Zendiator;

namespace Zendiator.Tests;

public sealed class EchoHandler : IRequestHandler<Echo, string?>
{
    public ValueTask<string?> HandleAsync(Echo request, CancellationToken cancellationToken) => new(request.Value);
}
