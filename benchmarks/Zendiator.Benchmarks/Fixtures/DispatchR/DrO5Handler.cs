namespace Zendiator.Benchmarks;

public sealed class DrO5Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrO5, ValueTask<int>>
{
    public ValueTask<int> Handle(DrO5 request, CancellationToken cancellationToken) => new(request.Value);
}
