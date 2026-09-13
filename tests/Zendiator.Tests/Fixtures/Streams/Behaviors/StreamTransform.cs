using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamTransform : IStreamPipelineBehavior<GetTransform, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetTransform request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetTransform, int>
    {
        await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
            yield return i * 10;
    }
}
