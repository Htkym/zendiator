namespace Zendiator.Benchmarks;

public sealed class MoO3Handler : global::Mediator.IRequestHandler<MoO3, int>
{
    public ValueTask<int> Handle(MoO3 request, CancellationToken cancellationToken) => new(request.Value);
}
