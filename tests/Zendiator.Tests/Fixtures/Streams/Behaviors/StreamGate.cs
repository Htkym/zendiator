using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamGate : IStreamPipelineBehavior<GetGated, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetGated request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetGated, int>
    {
        if (!request.Open) yield break;
        await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
            yield return i;
    }
}
