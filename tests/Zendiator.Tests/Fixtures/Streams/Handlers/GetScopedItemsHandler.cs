using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetScopedItemsHandler(Trace trace) : IStreamRequestHandler<GetScopedItems, int>, IAsyncDisposable
{
    public static int Disposed;
    public static int? FirstHash;
    public async IAsyncEnumerable<int> HandleAsync(GetScopedItems request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 0; i < request.Count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
        finally
        {
            FirstHash ??= trace.GetHashCode();
        }
    }
    public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposed); return default; }
}
