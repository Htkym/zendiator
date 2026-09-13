using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class SuspHandler : IRequestHandler<SuspReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public SuspHandler() => Interlocked.Increment(ref Constructions);

    public async ValueTask<int> HandleAsync(SuspReq request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}
