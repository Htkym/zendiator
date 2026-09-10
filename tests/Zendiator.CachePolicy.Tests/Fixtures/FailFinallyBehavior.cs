using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class FailFinallyBehavior : IPipelineBehavior<FailReq, int>
{
    public static int HandleCalls;
    public static int FinallyCalls;

    public async ValueTask<int> HandleAsync<TNext>(FailReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<FailReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        try
        {
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Increment(ref FinallyCalls);
        }
    }
}
