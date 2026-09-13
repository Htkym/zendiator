using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DependencyInjection;

/// <summary>Lazily captures dispatch dependencies for one mediator instance. DI owns their disposal.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ZendiatorServiceResolver : IDisposable
{
    private const int PageBits = 5;
    private const int PageSize = 1 << PageBits;
    private static int _nextSlot;
    private readonly IServiceProvider _provider;
    private readonly ZendiatorRootLifetime _root;
    private readonly object _initializationLock = new();
    private object?[]?[] _pages = [];
    private int _firstSlot = -1;
    private object? _firstValue;
    private int _disposed;

    /// <summary>Creates a dependency cache for a mediator bound to the supplied scope.</summary>
    public ZendiatorServiceResolver(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _root = provider.GetRequiredService<ZendiatorRootLifetime>();
    }

    private static class ServiceSlot<T>
    {
        internal static readonly int Index = Interlocked.Increment(ref _nextSlot) - 1;
    }

    /// <summary>Gets the first instance resolved for this service type, regardless of its DI lifetime.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredService<T>() where T : notnull
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0 || _root.IsDisposed, this);
        var slot = ServiceSlot<T>.Index;
        if (Volatile.Read(ref _firstSlot) == slot)
            return (T)_firstValue!;
        var pages = Volatile.Read(ref _pages);
        var pageIndex = slot >> PageBits;
        if ((uint)pageIndex < (uint)pages.Length && Volatile.Read(ref pages[pageIndex]) is { } page
            && Volatile.Read(ref page[slot & (PageSize - 1)]) is { } value)
            return (T)value;
        return ResolveSlow<T>(slot);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T ResolveSlow<T>(int slot) where T : notnull
    {
        // ponytail: cold activations serialize per mediator; split locks only if cold contention warrants it.
        // Monitor is reentrant so a synchronous factory can dispatch to a different route.
        lock (_initializationLock)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0 || _root.IsDisposed, this);
            if (_firstSlot == slot) return (T)_firstValue!;
            var pageIndex = slot >> PageBits;
            var offset = slot & (PageSize - 1);
            if (pageIndex < _pages.Length && _pages[pageIndex]?[offset] is { } existing)
                return (T)existing;

            var service = _provider.GetRequiredService<T>();
            // A reentrant factory may have populated another slot while resolving this service.
            if (_firstSlot == -1)
            {
                _firstValue = service;
                Volatile.Write(ref _firstSlot, slot);
                return service;
            }
            var pages = _pages;
            if (pageIndex >= pages.Length)
            {
                var expanded = new object?[]?[Math.Max(pageIndex + 1, pages.Length * 2)];
                Array.Copy(pages, expanded, pages.Length);
                Volatile.Write(ref _pages, expanded);
                pages = expanded;
            }
            if (pages[pageIndex] is not { } page)
            {
                page = new object?[PageSize];
                Volatile.Write(ref pages[pageIndex], page);
            }
            Volatile.Write(ref page[offset], service);
            return service;
        }
    }

    /// <summary>Stops dispatch without disposing dependencies owned by DI.</summary>
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}

/// <summary>Tracks normal root-container disposal for generated mediators.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ZendiatorRootLifetime : IDisposable
{
    private int _disposed;
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Marks the root container as disposed.</summary>
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
