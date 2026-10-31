using Zendiator;

namespace Zendiator.Allocation.Tests;

[ZendiatorHandler]
public sealed class AllocStreamHandler : IStreamSourceHandler<AllocStream, int, AllocCursor>
{
    public ValueTask<AllocCursor> OpenAsync(AllocStream request, CancellationToken cancellationToken) => new(new AllocCursor(request.Count, cancellationToken));
}

public struct AllocCursor(int count, CancellationToken token) : IAsyncEnumerator<int>
{
    private int _next;
    public int Current
    {
        get; private set;
    }
    public ValueTask<bool> MoveNextAsync()
    {
        token.ThrowIfCancellationRequested();
        if (_next == count)
            return new(false);
        Current = _next++;
        return new(true);
    }
    public ValueTask DisposeAsync() => default;
}
