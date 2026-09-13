using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class GateHandler : IRequestHandler<GateReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public GateHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(GateReq request, CancellationToken cancellationToken) => new(7);
}
