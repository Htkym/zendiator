using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DependencyInjection;

/// <summary>Lazily captures the sole dependency type of a generated mediator. DI owns its disposal.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ZendiatorSingleServiceResolver<T> : IDisposable where T : class
{
    private readonly ZendiatorRootLifetime _root;
    private readonly ZendiatorInitializationLock _initializationLock;
    private T? _value;
    private int _disposed;

    /// <summary>Creates a dependency cache for a mediator bound to the supplied scope.</summary>
    public ZendiatorSingleServiceResolver(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _initializationLock = new(provider);
        _root = provider.GetRequiredService<ZendiatorRootLifetime>();
    }

    /// <summary>Creates a dependency cache with the root lifetime supplied by DI.</summary>
    public ZendiatorSingleServiceResolver(IServiceProvider provider, ZendiatorRootLifetime root)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(root);
        _initializationLock = new(provider);
        _root = root;
    }

    /// <summary>Gets the first captured instance, regardless of its DI lifetime.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredService()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0 || _root.IsDisposed, typeof(ZendiatorServiceResolver));
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
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0 || _root.IsDisposed, typeof(ZendiatorServiceResolver));
            if (_value is { } existing) return existing;
            var service = initialization.Provider.GetRequiredService<T>();
            // A reentrant factory may already have published this type. Keep the first capture,
            // while returning this activation's result, as the general resolver does.
            if (_value is null) Volatile.Write(ref _value, service);
            return service;
        }
    }

    /// <summary>Stops dispatch without disposing the dependency owned by DI.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void Dispose() => Volatile.Write(ref _disposed, 1);
}
