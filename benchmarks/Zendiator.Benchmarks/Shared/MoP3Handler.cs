namespace Zendiator.Benchmarks;

public sealed class MoP3Handler : global::Mediator.IRequestHandler<MoP3, int>
{
    public ValueTask<int> Handle(MoP3 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
