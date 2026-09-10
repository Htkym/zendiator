using global::Zendiator;

namespace Zendiator.Tests;

public sealed class TokenBehavior : IPipelineBehavior<ChangeToken, CancellationToken>
{
    public ValueTask<CancellationToken> HandleAsync<TNext>(ChangeToken request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<ChangeToken, CancellationToken> => next.InvokeAsync(request, request.Replacement);
}
