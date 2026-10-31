using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

[ZendiatorHandler]
public sealed class DiNumbersHandler : IStreamSourceHandler<DiNumbers, int, DiNumbersCursor>
{
    public static int Calls;
    public ValueTask<DiNumbersCursor> OpenAsync(DiNumbers request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new DiNumbersCursor(request.Count, cancellationToken));
    }
}

public sealed class DiNumbersCursor(int count, CancellationToken token, Action? onDispose = null) : IAsyncEnumerator<int>
{
    private int _next;
    public int Current
    {
        get; private set;
    }
    public async ValueTask<bool> MoveNextAsync()
    {
        token.ThrowIfCancellationRequested();
        if (_next == count)
            return false;
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        Current = _next++;
        return true;
    }
    public ValueTask DisposeAsync()
    {
        onDispose?.Invoke();
        return default;
    }
}
