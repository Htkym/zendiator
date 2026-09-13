using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamFilter : IStreamPipelineBehavior<GetFilter, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetFilter request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetFilter, int>
    {
        await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
            if (i % 2 == 0) yield return i;
    }
}
