namespace Zendiator.Benchmarks;

public sealed class DrP5Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrP5, ValueTask<int>>
{
    public ValueTask<int> Handle(DrP5 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
