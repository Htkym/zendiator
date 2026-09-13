namespace Zendiator.Benchmarks;

public sealed class DrPingHandler : global::DispatchR.Abstractions.Send.IRequestHandler<DrPing, ValueTask<int>>
{
    public ValueTask<int> Handle(DrPing request, CancellationToken cancellationToken) => new(request.Value + 1);
}
