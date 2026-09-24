using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DependencyInjection;

/// <summary>Lazily captures the sole dependency type of a generated mediator. DI owns its disposal.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ZendiatorSingleServiceResolver<T> where T : class
{
    // ponytail: cold waiters share one monitor per Handler type; shard only if contention warrants it.
    private static readonly object WaitMonitor = new();
    private readonly IServiceProvider _provider;
    private T? _value;
    private int _ownerThreadId;
    private int _waiterCount;

    /// <summary>Creates a dependency cache for a mediator bound to the supplied scope.</summary>
    public ZendiatorSingleServiceResolver(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>Gets the first captured instance, regardless of its DI lifetime.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredService()
    {
        return Volatile.Read(ref _value) ?? ResolveSlow();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T ResolveSlow()
    {
        // Preserve slot order for callers of the non-generic resolver.
        ZendiatorServiceResolver.ReserveServiceSlot<T>();
        var threadId = Environment.CurrentManagedThreadId;
        while (true)
        {
            if (Volatile.Read(ref _value) is { } existing) return existing;
            var owner = Volatile.Read(ref _ownerThreadId);
            if (owner == threadId) return ResolveOnOwnerThread();
            if (owner == 0 && Interlocked.CompareExchange(ref _ownerThreadId, threadId, 0) == 0)
            {
                try
                {
                    if (_value is { } captured) return captured;
                    return ResolveOnOwnerThread();
                }
                finally
                {
                    // Exchange pairs with waiter registration so a new waiter cannot miss the pulse.
                    Interlocked.Exchange(ref _ownerThreadId, 0);
                    if (Volatile.Read(ref _waiterCount) != 0)
                    {
                        lock (WaitMonitor) Monitor.PulseAll(WaitMonitor);
                    }
                }
            }
            lock (WaitMonitor)
            {
                Interlocked.Increment(ref _waiterCount);
                try
                {
                    while (Volatile.Read(ref _ownerThreadId) != 0 && Volatile.Read(ref _value) is null)
                        Monitor.Wait(WaitMonitor);
                }
                finally
                {
                    Interlocked.Decrement(ref _waiterCount);
                }
            }
        }
    }

    private T ResolveOnOwnerThread()
    {
        var service = _provider.GetRequiredService<T>();
        // Reentrant activation may already have published its own instance.
        if (_value is null) Volatile.Write(ref _value, service);
        return service;
    }
}
