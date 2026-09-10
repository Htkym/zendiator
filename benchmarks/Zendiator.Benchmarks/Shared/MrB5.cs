namespace Zendiator.Benchmarks;

public sealed class MrB5<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker5
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B5++;
        return next();
    }
}
