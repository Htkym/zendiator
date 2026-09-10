namespace Zendiator.Benchmarks;

public sealed class MrB2<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker2
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B2++;
        return next();
    }
}
