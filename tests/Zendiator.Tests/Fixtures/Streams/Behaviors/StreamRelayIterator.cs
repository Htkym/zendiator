using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamRelayIterator(Trace trace) : IStreamPipelineBehavior<GetRelayMixed, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetRelayMixed request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetRelayMixed, int>
    {
        trace.Add("mi");
        try
        {
            await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
                yield return i;
        }
        finally { trace.Add("/mi"); }
    }
}
