namespace Zendiator.Benchmarks;

public sealed class MrAsyncPingHandler : global::MediatR.IRequestHandler<MrAsyncPing, int>
{
    public async Task<int> Handle(MrAsyncPing request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}
