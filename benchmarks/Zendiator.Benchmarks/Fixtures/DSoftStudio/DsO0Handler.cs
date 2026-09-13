namespace Zendiator.Benchmarks;

public sealed class DsO0Handler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsO0, int>
{
    public ValueTask<int> Handle(DsO0 request, CancellationToken cancellationToken) => new(request.Value);
}
