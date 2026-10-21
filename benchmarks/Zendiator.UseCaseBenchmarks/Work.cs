using System.Runtime.CompilerServices;

namespace Competitive;

public sealed class Counter { public int Value; public int Active; }

public sealed class Probe
{
    public List<int> Events { get; } = [];
    public int Handlers;
    public int Items;
    public int Disposals;
    public bool Fail;
    public bool FailRelay;
}

public interface IWork
{
    Probe? Probe { get; }
}

public interface ILevel1 : IWork;
public interface ILevel2 : ILevel1;
public interface ILevel3 : ILevel2;
public interface ILevel4 : ILevel3;
public interface ILevel5 : ILevel4;

// Markers for streams whose Behaviors only pre-process and return next's sequence.
public interface IRelayLevel1 : IWork;
public interface IRelayLevel2 : IRelayLevel1;
public interface IRelayLevel3 : IRelayLevel2;
public interface IRelayLevel4 : IRelayLevel3;
public interface IRelayLevel5 : IRelayLevel4;

public static class Work
{
    public static void Void(Counter counter, Probe? probe, CancellationToken token)
    {
        Handle(0, probe, token);
        counter.Value++;
    }

    public static async ValueTask Notify(Counter counter, Probe? probe, bool asynchronous, int id, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (++counter.Active != 1)
            throw new InvalidOperationException("Notification handlers overlapped");
        try
        {
            if (asynchronous) await Task.Yield();
            Void(counter, probe, token);
            probe?.Events.Add(id);
        }
        finally { counter.Active--; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Enter(IWork request, int id) => request.Probe?.Events.Add(id);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Relay(IWork request, int id)
    {
        if (request.Probe is not { } probe) return;
        if (probe.FailRelay)
            throw new ArithmeticException("Expected relay sentinel");
        probe.Events.Add(id);
    }

    public static int Handle(int value, Probe? probe, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (probe is not null)
        {
            probe.Handlers++;
            probe.Events.Add(100);
            if (probe.Fail)
                throw new ArithmeticException("Expected correctness sentinel");
        }
        return value + 1;
    }

    public static async IAsyncEnumerable<int> Stream(int count, bool asynchronous, Probe? probe,
        [EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (probe is not null)
        {
            probe.Handlers++;
            probe.Events.Add(100);
        }
        try
        {
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (asynchronous)
                    await Task.Yield();
                token.ThrowIfCancellationRequested();
                if (probe is not null)
                {
                    if (probe.Fail)
                        throw new ArithmeticException("Expected correctness sentinel");
                    probe.Items++;
                }
                yield return i;
            }
        }
        finally
        {
            if (probe is not null)
                probe.Disposals++;
        }
    }
}

// Native cursor for the new Zendiator protocol. Work.Stream above remains unchanged for peers.
public sealed class WorkStreamCursor(int count, bool asynchronous, Probe? probe,
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

// Reused only by sequential typed-terminal benchmark operations; results live in this class.
public sealed class StreamBenchmarkSink : Zendiator.IStreamSink<int>
{
    public int Sum { get; private set; }
    public int First { get; private set; }
    private bool _firstOnly;
    private bool _seen;
    public void Reset(bool firstOnly)
    {
        Sum = 0; First = -1; _firstOnly = firstOnly; _seen = false;
    }
    public ValueTask<bool> OnNextAsync(int item, CancellationToken token)
    {
        if (!_seen) { First = item; _seen = true; }
        Sum += item;
        return new(!_firstOnly);
    }
}
