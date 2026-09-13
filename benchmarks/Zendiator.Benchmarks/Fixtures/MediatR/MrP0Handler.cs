namespace Zendiator.Benchmarks;

public sealed class MrP0Handler : global::MediatR.IRequestHandler<MrP0, int>
{
    public Task<int> Handle(MrP0 request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}
