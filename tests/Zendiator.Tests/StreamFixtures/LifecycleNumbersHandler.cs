using global::Zendiator;

namespace Zendiator.Tests;

// Dedicated to StreamLifecycleTests so its counters are never touched by other test classes.
public sealed class LifecycleNumbersHandler : IStreamRequestHandler<LifecycleNumbers, int>
{
    public static int Starts;
    public async IAsyncEnumerable<int> HandleAsync(LifecycleNumbers request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Starts);
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}
