using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class ExpPipeHandler : IRequestHandler<ExpPipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public ExpPipeHandler() => Interlocked.Increment(ref Constructions);

    ValueTask<int> IRequestHandler<ExpPipeReq, int>.HandleAsync(ExpPipeReq request, CancellationToken cancellationToken) =>
        new(request.Value + 1);
}
