namespace Zendiator.Benchmarks;

public sealed class IhB2<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B2++;
        return Next(request, cancellationToken);
    }
}
