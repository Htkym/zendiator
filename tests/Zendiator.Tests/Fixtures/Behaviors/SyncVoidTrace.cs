using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SyncVoidTrace<TRequest>(Trace trace) : ISyncPipelineBehavior<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    public void Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest>
    {
        trace.Add("sync-void");
        try
        {
            next.Invoke(request, cancellationToken);
        }
        finally
        {
            trace.Add("/sync-void");
        }
    }
}
