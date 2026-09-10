namespace Zendiator.Benchmarks;

public sealed class MoPingHandler : global::Mediator.IRequestHandler<MoPing, int>
{
    public ValueTask<int> Handle(MoPing request, CancellationToken cancellationToken) => new(request.Value + 1);
}
