using global::Zendiator;

namespace Zendiator.Tests;

public sealed class AddOneRetryBehavior : ISyncPipelineBehavior<AddOne, int>
{
    public int Handle<TNext>(AddOne request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<AddOne, int>
    {
        next.Invoke(request, cancellationToken);
        return next.Invoke(request, cancellationToken);
    }
}
