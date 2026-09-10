using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.CachePolicy.Tests;

public sealed class ReenterBehavior : IPipelineBehavior<ReReq, int>
{
    public static int HandleCalls;
    private readonly IServiceProvider _services;

    public ReenterBehavior(IServiceProvider services) => _services = services;

    public async ValueTask<int> HandleAsync<TNext>(ReReq request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<ReReq, int>
    {
        Interlocked.Increment(ref HandleCalls);
        var inner = await _services.GetRequiredService<IZendiator>().SendAsync(new Val0(1), cancellationToken).ConfigureAwait(false);
        return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false) + inner;
    }
}
