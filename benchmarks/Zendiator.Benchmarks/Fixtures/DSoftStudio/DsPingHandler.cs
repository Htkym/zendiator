namespace Zendiator.Benchmarks;

public sealed class DsPingHandler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsPing, int>
{
    public ValueTask<int> Handle(DsPing request, CancellationToken cancellationToken) => new(request.Value + 1);
}
