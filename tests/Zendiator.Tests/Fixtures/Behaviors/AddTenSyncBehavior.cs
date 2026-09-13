using global::Zendiator;

namespace Zendiator.Tests;

public sealed class AddTenSyncBehavior : ISyncPipelineBehavior<AddOne, int>
{
    public int Handle<TNext>(AddOne request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<AddOne, int> =>
        next.Invoke(new AddOne(request.Value + 10), cancellationToken);
}
