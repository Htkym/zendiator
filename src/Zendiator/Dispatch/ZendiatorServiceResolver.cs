using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DependencyInjection;

/// <summary>Lazily captures dispatch dependencies for one mediator instance. DI owns their disposal.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ZendiatorServiceResolver
{
    private const int PageBits = 5;
    private const int PageSize = 1 << PageBits;
    private static int _nextSlot;
    private readonly ZendiatorInitializationLock _initializationLock;
    // Page zero is stored directly until a later page needs a jagged directory.
    private object?[] _pages = [];
    private int _firstSlot = -1;
    private readonly int _initialPageCapacity;
    private object? _firstValue;

    /// <summary>Creates a dependency cache for a mediator bound to the supplied scope.</summary>
    public ZendiatorServiceResolver(IServiceProvider provider) : this(provider, PageSize) { }

    /// <summary>Creates a dependency cache with a bounded initial page for a generated composition.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ZendiatorServiceResolver(IServiceProvider provider, int initialPageCapacity)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (initialPageCapacity < 1 || initialPageCapacity > PageSize)
            throw new ArgumentOutOfRangeException(nameof(initialPageCapacity));
        _initialPageCapacity = initialPageCapacity;
        _initializationLock = new(provider);
    }

    private static class ServiceSlot<T>
    {
        internal static readonly int Index = Interlocked.Increment(ref _nextSlot) - 1;
    }

    internal static void ReserveServiceSlot<T>() => _ = ServiceSlot<T>.Index;

    /// <summary>Gets the first instance resolved for this service type, regardless of its DI lifetime.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T GetRequiredService<T>() where T : notnull => GetRequiredServiceAtSlot<T>(ServiceSlot<T>.Index);

    /// <summary>Gets a captured dependency using a slot owned by a generated composition.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected T GetRequiredServiceAtSlot<T>(int slot) where T : notnull
    {
        if (Volatile.Read(ref _firstSlot) == slot)
            return (T)_firstValue!;
        if (GetCachedValue(slot) is { } value)
            return (T)value;
        return ResolveSlow<T>(slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object? GetCachedValue(int slot)
    {
        var storage = Volatile.Read(ref _pages);
        if (storage.GetType() == typeof(object[]))
            return (uint)slot < (uint)storage.Length ? Volatile.Read(ref storage[slot]) : null;
        var pages = (object?[]?[])storage;
        var pageIndex = slot >> PageBits;
        return (uint)pageIndex < (uint)pages.Length && Volatile.Read(ref pages[pageIndex]) is { } page
            ? Volatile.Read(ref page[slot & (PageSize - 1)]) : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T ResolveSlow<T>(int slot) where T : notnull
    {
        // ponytail: cold activations serialize per mediator; split locks only if cold contention warrants it.
        // Monitor is reentrant so a synchronous factory can dispatch to a different route.
        var initialization = _initializationLock;
        lock (initialization)
        {
            if (_firstSlot == slot) return (T)_firstValue!;
            var pageIndex = slot >> PageBits;
            var offset = slot & (PageSize - 1);
            if (GetCachedValue(slot) is { } existing)
                return (T)existing;

            var service = initialization.Provider.GetRequiredService<T>();
            // A reentrant factory may have populated another slot while resolving this service.
            if (_firstSlot == -1)
            {
                _firstValue = service;
                Volatile.Write(ref _firstSlot, slot);
                return service;
            }
            var storage = _pages;
            object?[]?[] pages;
            if (storage.GetType() == typeof(object[]))
            {
                if (pageIndex == 0)
                {
                    if (offset >= storage.Length)
                    {
                        var expanded = new object?[storage.Length == 0
                            ? Math.Max(_initialPageCapacity, offset + 1) : PageSize];
                        if (storage.Length != 0) Array.Copy(storage, expanded, storage.Length);
                        Volatile.Write(ref _pages, expanded);
                        storage = expanded;
                    }
                    Volatile.Write(ref storage[offset], service);
                    return service;
                }
                pages = new object?[]?[Math.Max(pageIndex + 1, storage.Length == 0 ? 0 : 2)];
                if (storage.Length != 0)
                {
                    // Promoted pages retain the full-page invariant used by readers.
                    if (storage.Length < PageSize)
                    {
                        var full = new object?[PageSize];
                        Array.Copy(storage, full, storage.Length);
                        storage = full;
                    }
                    pages[0] = storage;
                }
                Volatile.Write(ref _pages, pages);
            }
            else
            {
                pages = (object?[]?[])storage;
            }
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
}
