namespace Zendiator.Benchmarks;

public sealed class IhB1<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B1++;
        return Next(request, cancellationToken);
    }
}
