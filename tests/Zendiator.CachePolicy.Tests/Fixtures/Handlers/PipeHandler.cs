using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class PipeHandler : IRequestHandler<PipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public PipeHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(PipeReq request, CancellationToken cancellationToken) => new(request.Value + 1000);
}
