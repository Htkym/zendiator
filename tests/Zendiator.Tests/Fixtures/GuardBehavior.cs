using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GuardBehavior : IPipelineBehavior<GuardedDelete>
{
    public ValueTask HandleAsync<TNext>(GuardedDelete request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GuardedDelete> =>
        request.UserId > 0 ? next.InvokeAsync(request, cancellationToken) : default;
}
