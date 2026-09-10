namespace Zendiator.Benchmarks;

public sealed class DsP0Handler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsP0, int>
{
    public ValueTask<int> Handle(DsP0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
