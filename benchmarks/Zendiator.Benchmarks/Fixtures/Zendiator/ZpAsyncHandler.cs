using Zendiator;

namespace Zendiator.Benchmarks;

public sealed class ZpAsyncHandler : IRequestHandler<ZpAsync, int>
{
    public async ValueTask<int> HandleAsync(ZpAsync request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}
