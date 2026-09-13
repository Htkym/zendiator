using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class ExpHandler : IRequestHandler<ExpReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public ExpHandler() => Interlocked.Increment(ref Constructions);

    ValueTask<int> IRequestHandler<ExpReq, int>.HandleAsync(ExpReq request, CancellationToken cancellationToken) =>
        new(request.Value + 1);
}
