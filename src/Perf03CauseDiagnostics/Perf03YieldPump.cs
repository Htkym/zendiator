namespace Competitive.Perf03.Counted;

internal sealed class Perf03YieldPump : SynchronizationContext
{
    private readonly object sync = new();
    private readonly Queue<WorkItem> pending = new();
    private long posted;
    private long executed;

    internal int PendingCount
    {
        get
        {
            lock (sync)
                return pending.Count;
        }
    }

    internal long PostedCount
    {
        get
        {
            lock (sync)
                return posted;
        }
    }

    internal long ExecutedCount
    {
        get
        {
            lock (sync)
                return executed;
        }
    }

    public override SynchronizationContext CreateCopy() => this;

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        lock (sync)
            pending.Enqueue(new(++posted, d, state));
    }

    internal IDisposable Enter()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        return new ContextScope(previous);
    }

    internal long RunOne()
    {
        WorkItem work;
        lock (sync)
        {
            if (!pending.TryDequeue(out work))
                throw new InvalidOperationException("No held Task.Yield callback is available.");
            executed++;
        }
        work.Callback(work.State);
        return work.Ticket;
    }

    private readonly record struct WorkItem(long Ticket, SendOrPostCallback Callback, object? State);

    private sealed class ContextScope(SynchronizationContext? previous) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            SetSynchronizationContext(previous);
        }
    }
}
