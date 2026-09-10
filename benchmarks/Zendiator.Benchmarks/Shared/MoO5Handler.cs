namespace Zendiator.Benchmarks;

public sealed class MoO5Handler : global::Mediator.IRequestHandler<MoO5, int>
{
    public ValueTask<int> Handle(MoO5 request, CancellationToken cancellationToken) => new(request.Value);
}
