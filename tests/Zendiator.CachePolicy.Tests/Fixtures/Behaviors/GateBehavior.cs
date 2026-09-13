using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class GateBehavior : IPipelineBehavior<GateReq, int>
{
    public static int HandleCalls;

    public ValueTask<int> HandleAsync<TNext>(GateReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GateReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        return request.Open ? next.InvokeAsync(request, cancellationToken) : new(-1);
    }
}
