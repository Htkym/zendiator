namespace Zendiator.Benchmarks;

public sealed class MoP1Handler : global::Mediator.IRequestHandler<MoP1, int>
{
    public ValueTask<int> Handle(MoP1 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
