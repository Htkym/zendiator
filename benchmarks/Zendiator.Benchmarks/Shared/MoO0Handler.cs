namespace Zendiator.Benchmarks;

public sealed class MoO0Handler : global::Mediator.IRequestHandler<MoO0, int>
{
    public ValueTask<int> Handle(MoO0 request, CancellationToken cancellationToken) => new(request.Value);
}
