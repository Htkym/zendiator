namespace Zendiator.Benchmarks;

public sealed class IhB4<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B4++;
        return Next(request, cancellationToken);
    }
}
