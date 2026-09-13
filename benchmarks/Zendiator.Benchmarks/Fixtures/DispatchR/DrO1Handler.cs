namespace Zendiator.Benchmarks;

public sealed class DrO1Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrO1, ValueTask<int>>
{
    public ValueTask<int> Handle(DrO1 request, CancellationToken cancellationToken) => new(request.Value);
}
