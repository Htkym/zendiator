using global::Zendiator;

namespace Zendiator.Tests;

public sealed class MaybeGateBehavior : IPipelineBehavior<GetMaybe, int>
{
    public ValueTask<int> HandleAsync<TNext>(GetMaybe request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GetMaybe, int> =>
        request.Open ? next.InvokeAsync(request, cancellationToken) : new(0);
}
