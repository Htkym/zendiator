using global::Zendiator;

namespace Zendiator.Tests;

public sealed class RetryHandler : IRequestHandler<Retry, int>
{
    private int _calls;
    public ValueTask<int> HandleAsync(Retry request, CancellationToken cancellationToken) => new(++_calls);
}
