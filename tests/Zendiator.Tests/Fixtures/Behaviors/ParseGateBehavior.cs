using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ParseGateBehavior : ISyncPipelineBehavior<ParseRequest, int>
{
    public int Handle<TNext>(scoped ParseRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<ParseRequest, int> =>
        request.Data.Length > 0 ? next.Invoke(request, cancellationToken) : -1;
}
