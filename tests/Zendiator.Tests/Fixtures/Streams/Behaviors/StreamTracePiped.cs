using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamTracePiped(Trace trace) : StreamTraced(trace, "spipe"), IStreamPipelineBehavior<GetPiped, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped, int>
    {
        Before();
        try
        {
            await foreach (var item in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally { After(); }
    }
}
