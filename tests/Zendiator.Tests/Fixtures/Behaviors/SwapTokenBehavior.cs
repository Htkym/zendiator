using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SwapTokenBehavior : IPipelineBehavior<SeenToken>
{
    public ValueTask HandleAsync<TNext>(SeenToken request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<SeenToken> =>
        next.InvokeAsync(request, new CancellationTokenSource().Token);
}
