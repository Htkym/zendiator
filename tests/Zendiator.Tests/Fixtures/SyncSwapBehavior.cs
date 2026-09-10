using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SyncSwapBehavior : ISyncPipelineBehavior<SeenSync>
{
    public void Handle<TNext>(SeenSync request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<SeenSync> =>
        next.Invoke(request, new CancellationTokenSource().Token);
}
