using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetLazyHandler : IStreamRequestHandler<GetLazy, int>
{
    public static int Started;
    public static int Items;
    public async IAsyncEnumerable<int> HandleAsync(GetLazy request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Started);
        for (var i = 0; i < request.Count; i++)
        {
            Interlocked.Increment(ref Items);
            await Task.Yield();
            yield return i;
        }
    }
}
