using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class DiStreamBehavior<TRequest, TItem> : IStreamMiddleware<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public static int Calls;
    public ValueTask<IAsyncEnumerator<TItem>> OpenAsync(TRequest request, IStreamUpstream<TRequest, TItem> upstream, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return upstream.OpenAsync(request, cancellationToken);
    }
}
