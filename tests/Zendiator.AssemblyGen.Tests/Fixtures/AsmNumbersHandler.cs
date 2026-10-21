using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

[ZendiatorHandler]
public sealed class AsmNumbersHandler : IStreamSourceHandler<AsmNumbers, int, AsmNumbersCursor>
{
    public ValueTask<AsmNumbersCursor> OpenAsync(AsmNumbers request, CancellationToken cancellationToken) =>
        new(new AsmNumbersCursor(request.Count, cancellationToken));
}

public sealed class AsmNumbersCursor(int count, CancellationToken token) : IAsyncEnumerator<int>
{
    private int _next;
    public int Current { get; private set; }
    public async ValueTask<bool> MoveNextAsync()
    {
        token.ThrowIfCancellationRequested();
        if (_next == count) return false;
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        Current = _next++;
        return true;
    }
    public ValueTask DisposeAsync() => default;
}
