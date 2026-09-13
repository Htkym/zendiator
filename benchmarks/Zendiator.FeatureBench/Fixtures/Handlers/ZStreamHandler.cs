using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZStreamHandler : IStreamRequestHandler<ZStream, int>
{
    public async IAsyncEnumerable<int> HandleAsync(ZStream request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
        await Task.CompletedTask;
    }
}
