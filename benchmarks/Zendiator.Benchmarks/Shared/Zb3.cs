using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zb3<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker3
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B3++;
        return next.InvokeAsync(request, cancellationToken);
    }
}
