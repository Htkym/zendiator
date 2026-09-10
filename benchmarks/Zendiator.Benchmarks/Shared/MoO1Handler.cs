namespace Zendiator.Benchmarks;

public sealed class MoO1Handler : global::Mediator.IRequestHandler<MoO1, int>
{
    public ValueTask<int> Handle(MoO1 request, CancellationToken cancellationToken) => new(request.Value);
}
