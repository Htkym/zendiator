namespace Zendiator.Benchmarks;

public sealed class DrP3Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrP3, ValueTask<int>>
{
    public ValueTask<int> Handle(DrP3 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
