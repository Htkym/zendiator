namespace Zendiator.Benchmarks;

public sealed class DrO0Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrO0, ValueTask<int>>
{
    public ValueTask<int> Handle(DrO0 request, CancellationToken cancellationToken) => new(request.Value);
}
