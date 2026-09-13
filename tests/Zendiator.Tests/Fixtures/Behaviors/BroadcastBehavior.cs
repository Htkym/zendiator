using global::Zendiator;

namespace Zendiator.Tests;

public sealed class BroadcastBehavior : IPipelineBehavior<Broadcast>
{
    public static int Calls;
    public ValueTask HandleAsync<TNext>(Broadcast request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<Broadcast>
    {
        Interlocked.Increment(ref Calls);
        return next.InvokeAsync(request, cancellationToken);
    }
}
