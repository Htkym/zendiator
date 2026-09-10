namespace Zendiator.Benchmarks;

public sealed class MoP0Handler : global::Mediator.IRequestHandler<MoP0, int>
{
    public ValueTask<int> Handle(MoP0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
