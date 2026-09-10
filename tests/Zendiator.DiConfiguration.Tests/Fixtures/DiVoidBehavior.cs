using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class DiVoidBehavior<TRequest> : IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public static int Calls;
    public ValueTask HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest>
    {
        Interlocked.Increment(ref Calls);
        return next.InvokeAsync(request, cancellationToken);
    }
}
