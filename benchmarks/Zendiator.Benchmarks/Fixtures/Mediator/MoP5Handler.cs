namespace Zendiator.Benchmarks;

public sealed class MoP5Handler : global::Mediator.IRequestHandler<MoP5, int>
{
    public ValueTask<int> Handle(MoP5 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
