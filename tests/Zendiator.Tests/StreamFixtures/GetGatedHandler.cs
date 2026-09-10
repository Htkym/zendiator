using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetGatedHandler : IStreamRequestHandler<GetGated, int>
{
    public static int Calls;
    public async IAsyncEnumerable<int> HandleAsync(GetGated request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
