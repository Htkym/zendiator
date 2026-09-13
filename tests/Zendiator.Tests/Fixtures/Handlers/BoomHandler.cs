using global::Zendiator;

namespace Zendiator.Tests;

public sealed class BoomHandler : IRequestHandler<Boom>
{
    public static readonly InvalidOperationException Failure = new("void boom");
    public ValueTask HandleAsync(Boom request, CancellationToken cancellationToken) => throw Failure;
}
