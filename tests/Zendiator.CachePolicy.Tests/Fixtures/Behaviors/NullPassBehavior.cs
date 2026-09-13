using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class NullPassBehavior : IPipelineBehavior<RefReq, string?>
{
    public static int HandleCalls;

    public ValueTask<string?> HandleAsync<TNext>(RefReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<RefReq, string?>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(null!, cancellationToken);
    }
}
