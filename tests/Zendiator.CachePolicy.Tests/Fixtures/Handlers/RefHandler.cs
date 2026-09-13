using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class RefHandler : IRequestHandler<RefReq, string?>
{
    public static int FactoryCalls;
    public static int Constructions;

    public RefHandler() => Interlocked.Increment(ref Constructions);

    public ValueTask<string?> HandleAsync(RefReq request, CancellationToken cancellationToken) => new(request.Text);
}
