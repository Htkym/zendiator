using global::Zendiator;

namespace Zendiator.Tests;

public sealed class FlakyRetryBehavior : IPipelineBehavior<FlakyCommand>
{
    public async ValueTask HandleAsync<TNext>(FlakyCommand request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<FlakyCommand>
    {
        await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
