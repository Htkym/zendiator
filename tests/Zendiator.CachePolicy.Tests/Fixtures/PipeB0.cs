using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class PipeB0 : IPipelineBehavior<PipeReq, int>
{
    public static int FactoryCalls;
    public static int Constructions;
    public static int HandleCalls;

    public PipeB0() => Interlocked.Increment(ref Constructions);

    public ValueTask<int> HandleAsync<TNext>(PipeReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<PipeReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        return next.InvokeAsync(request, cancellationToken);
    }
}
