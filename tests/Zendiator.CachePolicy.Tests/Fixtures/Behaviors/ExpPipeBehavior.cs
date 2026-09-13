using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class ExpPipeBehavior : IPipelineBehavior<ExpPipeReq, int>
{
    public static int HandleCalls;

    ValueTask<int> IPipelineBehavior<ExpPipeReq, int>.HandleAsync<TNext>(ExpPipeReq request, TNext next, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(request, cancellationToken);
    }
}
