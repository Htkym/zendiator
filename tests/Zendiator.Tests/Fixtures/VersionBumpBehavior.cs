using global::Zendiator;

namespace Zendiator.Tests;

public sealed class VersionBumpBehavior : IPipelineBehavior<GetVersion, int>
{
    public ValueTask<int> HandleAsync<TNext>(GetVersion request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GetVersion, int> =>
        next.InvokeAsync(new GetVersion(request.V + 100), cancellationToken);
}
