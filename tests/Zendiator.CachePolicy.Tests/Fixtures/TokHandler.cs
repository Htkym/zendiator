using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class TokHandler : IRequestHandler<TokReq, CancellationToken>
{
    public static int FactoryCalls;
    public static int Constructions;

    public TokHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<CancellationToken> HandleAsync(TokReq request, CancellationToken cancellationToken) =>
        new(cancellationToken);
}
