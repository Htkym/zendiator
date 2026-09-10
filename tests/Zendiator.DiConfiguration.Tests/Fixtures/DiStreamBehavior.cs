using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class DiStreamBehavior<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public static int Calls;
    public async IAsyncEnumerable<TItem> HandleAsync<TNext>(TRequest request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<TRequest, TItem>
    {
        Interlocked.Increment(ref Calls);
        await foreach (var item in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
            yield return item;
    }
}
