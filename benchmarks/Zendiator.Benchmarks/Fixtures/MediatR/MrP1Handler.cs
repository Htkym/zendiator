namespace Zendiator.Benchmarks;

public sealed class MrP1Handler : global::MediatR.IRequestHandler<MrP1, int>
{
    public Task<int> Handle(MrP1 request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}
