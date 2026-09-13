using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zb2<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker2
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B2++;
        return next.InvokeAsync(request, cancellationToken);
    }
}
