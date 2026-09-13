using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class DiSyncBehavior<TRequest, TResponse> : ISyncPipelineBehavior<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    public static int Calls;
    public TResponse Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest, TResponse>
    {
        Interlocked.Increment(ref Calls);
        return next.Invoke(request, cancellationToken);
    }
}
