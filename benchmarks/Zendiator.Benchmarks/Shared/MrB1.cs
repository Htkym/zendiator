namespace Zendiator.Benchmarks;

public sealed class MrB1<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker1
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B1++;
        return next();
    }
}
