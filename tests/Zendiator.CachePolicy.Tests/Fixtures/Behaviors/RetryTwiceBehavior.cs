using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class RetryTwiceBehavior : IPipelineBehavior<RetryReq, int>
{
    public static int HandleCalls;

    public async ValueTask<int> HandleAsync<TNext>(RetryReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<RetryReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
