using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zb1<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker1
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B1++;
        return next.InvokeAsync(request, cancellationToken);
    }
}
