namespace Zendiator.Benchmarks;

public sealed class MoAsyncPingHandler : global::Mediator.IRequestHandler<MoAsyncPing, int>
{
    public async ValueTask<int> Handle(MoAsyncPing request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}
