using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ParseBumpBehavior : ISyncPipelineBehavior<ParseRequest, int>
{
    public int Handle<TNext>(ParseRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<ParseRequest, int> =>
        next.Invoke(request, cancellationToken) + 1000;
}
