using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SyncTrace<TRequest, TResponse>(Trace trace) : ISyncPipelineBehavior<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
    where TResponse : struct
{
    public TResponse Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest, TResponse>
    {
        trace.Add("sync");
        try
        {
            return next.Invoke(request, cancellationToken);
        }
        finally
        {
            trace.Add("/sync");
        }
    }
}
