using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class AddTenBehavior : IPipelineBehavior<AddReq, int>
{
    public static int HandleCalls;

    public ValueTask<int> HandleAsync<TNext>(AddReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<AddReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(new AddReq(request.Value + 10), cancellationToken);
    }
}
