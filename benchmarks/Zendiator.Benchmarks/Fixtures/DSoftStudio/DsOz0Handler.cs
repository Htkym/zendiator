namespace Zendiator.Benchmarks;

public sealed class DsOz0Handler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsOz0, int>
{
    public ValueTask<int> Handle(DsOz0 request, CancellationToken cancellationToken) => new(request.Value);
}
