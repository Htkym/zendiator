using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class FailHandler : IRequestHandler<FailReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;

    public FailHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(FailReq request, CancellationToken cancellationToken) =>
        throw CacheFixtureErrors.Instance;
}
