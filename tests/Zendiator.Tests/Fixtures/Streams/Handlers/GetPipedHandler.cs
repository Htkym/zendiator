using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetPipedHandler : IStreamRequestHandler<GetPiped, int>
{
    public static int Calls;
    public async IAsyncEnumerable<int> HandleAsync(GetPiped request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}
