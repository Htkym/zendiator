using System.Runtime.CompilerServices;

namespace Competitive;

// Only the pending async method uses the BCL pooled builder; the cursor itself is never pooled.
public sealed class PooledWorkStreamCursor(int count, bool asynchronous, Probe? probe,
    CancellationToken token) : IAsyncEnumerator<int>
{
    private int _index;
    private bool _started;
    private bool _finished;
    public int Current { get; private set; }

    public ValueTask<bool> MoveNextAsync()
    {
        if (_finished) return new(false);
        if (!_started)
        {
            token.ThrowIfCancellationRequested();
            _started = true;
            if (probe is not null) { probe.Handlers++; probe.Events.Add(100); }
        }
        if (_index >= count) { Finish(); return new(false); }
        if (asynchronous) return MovePendingAsync();
        try
        {
            // Preserve Work.Stream's checks on both sides of its optional suspension.
            token.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            return new(Publish());
        }
        catch { Finish(); throw; }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<bool> MovePendingAsync()
    {
        try
        {
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            return Publish();
        }
        catch { Finish(); throw; }
    }

    private bool Publish()
    {
        if (probe is not null)
        {
            if (probe.Fail) throw new ArithmeticException("Expected correctness sentinel");
            probe.Items++;
        }
        Current = _index++;
        return true;
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        if (_started && probe is not null) probe.Disposals++;
    }

    public ValueTask DisposeAsync() { Finish(); return default; }
}
