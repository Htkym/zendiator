namespace Zendiator.Benchmarks;

public sealed class DrO3Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrO3, ValueTask<int>>
{
    public ValueTask<int> Handle(DrO3 request, CancellationToken cancellationToken) => new(request.Value);
}
