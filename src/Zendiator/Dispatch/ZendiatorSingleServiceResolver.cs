using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DependencyInjection;

/// <summary>Lazily captures the sole dependency type of a generated mediator. DI owns its disposal.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ZendiatorSingleServiceResolver<T> where T : class
{
    private readonly ZendiatorInitializationLock _initializationLock;
    private T? _value;

    /// <summary>Creates a dependency cache for a mediator bound to the supplied scope.</summary>
    public ZendiatorSingleServiceResolver(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _initializationLock = new(provider);
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
        // Preserve first-resolution slot order for other mediators using the paged cache.
        ZendiatorServiceResolver.ReserveServiceSlot<T>();
        var initialization = _initializationLock;
        lock (initialization)
        {
            if (_value is { } existing) return existing;
            var service = initialization.Provider.GetRequiredService<T>();
            // A reentrant factory may already have published this type. Keep the first capture,
            // while returning this activation's result, as the general resolver does.
            if (_value is null) Volatile.Write(ref _value, service);
            return service;
        }
    }
}
