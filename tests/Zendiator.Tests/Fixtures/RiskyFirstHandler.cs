using global::Zendiator;

namespace Zendiator.Tests;

public sealed class RiskyFirstHandler : IRequestHandler<GetRisky, int>
{
    public static readonly InvalidOperationException Failure = new("risky boom");
    public ValueTask<int> HandleAsync(GetRisky request, CancellationToken cancellationToken) => throw Failure;
}
