namespace Zendiator.Benchmarks;

public sealed class DrP1Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrP1, ValueTask<int>>
{
    public ValueTask<int> Handle(DrP1 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
