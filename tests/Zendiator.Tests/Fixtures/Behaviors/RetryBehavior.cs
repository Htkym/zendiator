using global::Zendiator;

namespace Zendiator.Tests;

public sealed class RetryBehavior : IPipelineBehavior<Retry, int>
{
    public async ValueTask<int> HandleAsync<TNext>(Retry request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<Retry, int>
    {
        await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
