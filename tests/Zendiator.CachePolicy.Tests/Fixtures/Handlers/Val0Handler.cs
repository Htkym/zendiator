using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class Val0Handler : IRequestHandler<Val0, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public readonly Guid Id = Guid.NewGuid();

    public Val0Handler() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync(Val0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}
