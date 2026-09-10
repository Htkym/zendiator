using Zendiator;

namespace Zendiator.Allocation.Tests;

/// <summary>Pass-through behavior for Ping only: no logging, allocation or async state machine.</summary>
public sealed class PassThrough : IPipelineBehavior<Ping, int>
{
    public ValueTask<int> HandleAsync<TNext>(Ping request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<Ping, int>
        => next.InvokeAsync(request, cancellationToken);
}
