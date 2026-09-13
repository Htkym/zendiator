using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class SuspPipeHandler : IRequestHandler<SuspPipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public SuspPipeHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(SuspPipeReq request, CancellationToken cancellationToken) =>
        new(request.Value + 1);
}
