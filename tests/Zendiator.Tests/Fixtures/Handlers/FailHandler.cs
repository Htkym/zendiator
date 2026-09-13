using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FailHandler : IRequestHandler<Fail, int>
{
    public ValueTask<int> HandleAsync(Fail request, CancellationToken cancellationToken) => throw request.Error;
}
