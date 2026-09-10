using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SeenTokenHandler : IRequestHandler<SeenToken>
{
    public static CancellationToken Seen;
    public ValueTask HandleAsync(SeenToken request, CancellationToken cancellationToken)
    {
        Seen = cancellationToken;
        return default;
    }
}
