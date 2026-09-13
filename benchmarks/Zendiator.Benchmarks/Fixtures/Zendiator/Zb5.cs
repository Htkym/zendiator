using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zb5<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker5
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B5++;
        return next.InvokeAsync(request, cancellationToken);
    }
}
