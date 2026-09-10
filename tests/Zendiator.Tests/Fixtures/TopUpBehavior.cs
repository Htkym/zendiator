using global::Zendiator;

namespace Zendiator.Tests;

public sealed class TopUpBehavior : IPipelineBehavior<TopUp>
{
    public ValueTask HandleAsync<TNext>(TopUp request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TopUp> =>
        next.InvokeAsync(new TopUp(request.UserId + 10), cancellationToken);
}
