using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class Zb4<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>, IZrMarker4
{
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        ZrCounters.B4++;
        return next.InvokeAsync(request, cancellationToken);
    }
}
