using global::Zendiator;

namespace Zendiator.Tests;

public sealed class TokenHandler : IRequestHandler<ChangeToken, CancellationToken>
{
    public ValueTask<CancellationToken> HandleAsync(ChangeToken request, CancellationToken cancellationToken) => new(cancellationToken);
}
