using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class DispHandler : IRequestHandler<DispReq, int>, IAsyncDisposable
{
    public static int FactoryCalls;
    public static int Constructions;
    public static int DisposeCount;

    public DispHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(DispReq request, CancellationToken cancellationToken) => new(5);

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref DisposeCount);
        return default;
    }
}
