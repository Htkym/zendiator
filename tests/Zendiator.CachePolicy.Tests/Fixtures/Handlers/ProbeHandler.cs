using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class ProbeHandler : IRequestHandler<ProbeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public int Marker;

    public ProbeHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(ProbeReq request, CancellationToken cancellationToken) => new(Marker);
}
