namespace Zendiator.Benchmarks;

public sealed class MrB4<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker4
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B4++;
        return next();
    }
}
