namespace Zendiator.Benchmarks;

public sealed class MrP3Handler : global::MediatR.IRequestHandler<MrP3, int>
{
    public Task<int> Handle(MrP3 request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}
