using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class Guid0Handler : IRequestHandler<Guid0, Guid>
{
    public static int FactoryCalls;
    public static int Constructions;
    public readonly Guid Id = Guid.NewGuid();

    public Guid0Handler() => Interlocked.Increment(ref Constructions);

    public ValueTask<Guid> HandleAsync(Guid0 request, CancellationToken cancellationToken) => new(Id);
}
