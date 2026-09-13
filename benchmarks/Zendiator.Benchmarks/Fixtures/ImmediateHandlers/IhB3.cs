namespace Zendiator.Benchmarks;

public sealed class IhB3<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B3++;
        return Next(request, cancellationToken);
    }
}
