using global::Zendiator;

namespace Zendiator.Tests;

public sealed class DelayedHandler : IRequestHandler<Delayed, int>
{
    public async ValueTask<int> HandleAsync(Delayed request, CancellationToken cancellationToken) =>
        await request.Pending.WaitAsync(cancellationToken).ConfigureAwait(false);
}
