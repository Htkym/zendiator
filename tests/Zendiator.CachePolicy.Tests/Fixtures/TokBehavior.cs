using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class TokBehavior : IPipelineBehavior<TokReq, CancellationToken>
{
    public static int HandleCalls;

    public ValueTask<CancellationToken> HandleAsync<TNext>(TokReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TokReq, CancellationToken>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(request, request.Replacement);
    }
}
