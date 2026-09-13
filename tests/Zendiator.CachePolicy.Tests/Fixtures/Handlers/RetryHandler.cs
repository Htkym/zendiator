using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class RetryHandler : IRequestHandler<RetryReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    private int _calls;

    public RetryHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(RetryReq request, CancellationToken cancellationToken) =>
        new(Interlocked.Increment(ref _calls));
}
