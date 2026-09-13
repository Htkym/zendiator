using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class SuspBehavior : IPipelineBehavior<SuspPipeReq, int>
{
    public static int HandleCalls;
    public static int FinallyCalls;

    public async ValueTask<int> HandleAsync<TNext>(SuspPipeReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<SuspPipeReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        try
        {
            await Task.Yield();
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Increment(ref FinallyCalls);
        }
    }
}
