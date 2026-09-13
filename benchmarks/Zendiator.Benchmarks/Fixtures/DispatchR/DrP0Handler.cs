namespace Zendiator.Benchmarks;

public sealed class DrP0Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrP0, ValueTask<int>>
{
    public ValueTask<int> Handle(DrP0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
