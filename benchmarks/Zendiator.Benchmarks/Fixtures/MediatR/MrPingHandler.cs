namespace Zendiator.Benchmarks;

public sealed class MrPingHandler : global::MediatR.IRequestHandler<MrPing, int>
{
    public Task<int> Handle(MrPing request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}
