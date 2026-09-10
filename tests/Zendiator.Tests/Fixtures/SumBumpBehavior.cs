using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SumBumpBehavior : ISyncPipelineBehavior<SumSync, int>
{
    public int Handle<TNext>(SumSync request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<SumSync, int> =>
        next.Invoke(request, cancellationToken) + 1;
}
