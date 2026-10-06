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
