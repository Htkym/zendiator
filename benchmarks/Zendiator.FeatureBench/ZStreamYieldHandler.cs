using Zendiator;

namespace Zendiator.FeatureBench;

public sealed class ZStreamYieldHandler
{
    // Truly async variant for yield-shape comparison; not registered (duplicate would error).
    // Invoked directly as Direct baseline only.
    public async IAsyncEnumerable<int> HandleAsync(ZStream request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
