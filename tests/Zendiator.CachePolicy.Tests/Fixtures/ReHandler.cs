using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class ReHandler : IRequestHandler<ReReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public ReHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(ReReq request, CancellationToken cancellationToken) =>
        new(1000 + request.Value);
}
