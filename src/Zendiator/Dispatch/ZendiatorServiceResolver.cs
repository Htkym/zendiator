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
    private const int CapacityBits = 6;
    private const int CapacityMask = (1 << CapacityBits) - 1;
    private const int MaxSingleSlot = (int.MaxValue >> CapacityBits) - 1;
    private static int _nextSlot;
    private static readonly object InitializationMonitor = new();
    private static int _initializationWaiters;
    private readonly IServiceProvider _provider;
    // Null, one capture whose runtime type equals its service type, page zero, or a jagged page directory.
    private object? _storage;
    private int _initializingThread;
    // The initial page capacity, and above it the slot of a capture held alone.
    private int _layout;

    /// <summary>Creates a dependency cache for a mediator bound to the supplied scope.</summary>
    public ZendiatorServiceResolver(IServiceProvider provider) : this(provider, PageSize) { }

    /// <summary>Creates a dependency cache with a bounded initial page for a generated composition.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ZendiatorServiceResolver(IServiceProvider provider, int initialPageCapacity)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (initialPageCapacity < 1 || initialPageCapacity > PageSize)
            throw new ArgumentOutOfRangeException(nameof(initialPageCapacity));
        _layout = initialPageCapacity;
        _provider = provider;
    }

    private static class ServiceSlot<T>
    {
        internal static readonly int Index = Interlocked.Increment(ref _nextSlot) - 1;
    }

    internal static void ReserveServiceSlot<T>() => _ = ServiceSlot<T>.Index;

    /// <summary>Gets the first instance resolved for this service type, regardless of its DI lifetime.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public virtual T GetRequiredService<T>() where T : notnull => GetSharedServiceAtSlot<T>(ServiceSlot<T>.Index);

    /// <summary>Gets a captured dependency using a slot owned by a generated composition.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected T GetRequiredServiceAtSlot<T>(int slot) where T : notnull
        => GetCaptured<T>(slot) is { } value ? (T)value : ResolveSlow<T>(slot);

    /// <summary>Gets a dependency for callers outside generated routes, sharing captures held by route sets.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected T GetSharedServiceAtSlot<T>(int slot) where T : notnull
        => GetCaptured<T>(slot) is { } value ? (T)value : ResolveSharedSlow<T>(slot);

    /// <summary>Gets a generated route dependency set, or null before the route first uses it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected TSet? GetDependencySetAtSlot<TSet>(int slot) where TSet : class
        => typeof(TSet) != typeof(object[]) && typeof(TSet) != typeof(object?[]?[])
            && Volatile.Read(ref _storage) is { } storage && storage.GetType() == typeof(TSet)
            ? Unsafe.As<TSet>(storage) : GetPagedDependencySet<TSet>(slot);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private TSet? GetPagedDependencySet<TSet>(int slot) where TSet : class
        => Volatile.Read(ref _storage) is { } storage ? (TSet?)GetPagedValue(storage, slot) : null;

    /// <summary>Publishes a generated route dependency set unless another call published one first.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected TSet AddDependencySetAtSlot<TSet>(int slot, TSet dependencies) where TSet : class
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        var owner = EnterInitialization();
        try
        {
            if (GetCaptured<TSet>(slot) is { } existing) return (TSet)existing;
            Publish(slot, dependencies, typeof(TSet));
            return dependencies;
        }
        finally
        {
            if (owner) ExitInitialization();
        }
    }

    /// <summary>Publishes a generated route dependency set and captures its first dependency in the same initialization.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    protected TSet AddDependencySetAtSlot<TSet>(int slot, TSet dependencies, Action<TSet> captureFirst) where TSet : class
    {
        ArgumentNullException.ThrowIfNull(captureFirst);
        var owner = EnterInitialization();
        try
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            // This overload already owns the initialization; publish without entering it again.
            var published = dependencies;
            if (GetCaptured<TSet>(slot) is { } existing) published = (TSet)existing;
            else Publish(slot, dependencies, typeof(TSet));
            captureFirst(published);
            return published;
        }
        finally
        {
            if (owner) ExitInitialization();
        }
    }

    /// <summary>Captures a dependency into a field of a generated route dependency set.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public T ResolveDependency<T>(ref T? field) where T : class
    {
        var owner = EnterInitialization();
        try
        {
            if (Volatile.Read(ref field) is { } existing) return existing;
            var service = _provider.GetRequiredService<T>();
            if (Volatile.Read(ref field) is null) Volatile.Write(ref field, service);
            return service;
        }
        finally
        {
            if (owner) ExitInitialization();
        }
    }

    /// <summary>Returns the capture of a dependency kept in a generated route set, or null for other types.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual object? GetGeneratedService(Type serviceType) => null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object? GetCaptured<T>(int slot)
    {
        if (Volatile.Read(ref _storage) is not { } storage) return null;
        if (typeof(T) != typeof(object[]) && typeof(T) != typeof(object?[]?[]) && storage.GetType() == typeof(T)) return storage;
        return GetPagedValue(storage, slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? GetPagedValue(object storage, int slot)
    {
        if (storage.GetType() == typeof(object[]))
        {
            var page = Unsafe.As<object?[]>(storage);
            return (uint)slot < (uint)page.Length ? Volatile.Read(ref page[slot]) : null;
        }
        if (storage.GetType() != typeof(object?[]?[])) return null;
        var pages = Unsafe.As<object?[]?[]>(storage);
        var pageIndex = slot >> PageBits;
        return (uint)pageIndex < (uint)pages.Length && Volatile.Read(ref pages[pageIndex]) is { } target
            ? Volatile.Read(ref target[slot & (PageSize - 1)]) : null;
    }

    private static bool IsPageStorage(object storage) => storage.GetType() == typeof(object[]) || storage.GetType() == typeof(object?[]?[]);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T ResolveSlow<T>(int slot) where T : notnull
    {
        var owner = EnterInitialization();
        try
        {
            if (GetCaptured<T>(slot) is { } existing) return (T)existing;
            var service = _provider.GetRequiredService<T>();
            // A reentrant factory may have populated another slot while resolving this service.
            if (GetCaptured<T>(slot) is null) Publish(slot, service, typeof(T));
            return service;
        }
        finally
        {
            if (owner) ExitInitialization();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T ResolveSharedSlow<T>(int slot) where T : notnull
    {
        if (GetGeneratedService(typeof(T)) is not { } generated) return ResolveSlow<T>(slot);
        var owner = EnterInitialization();
        try
        {
            if (GetCaptured<T>(slot) is { } existing) return (T)existing;
            Publish(slot, generated, typeof(T));
            return (T)generated;
        }
        finally
        {
            if (owner) ExitInitialization();
        }
    }

    // Readers never lock, so a new array is published only after it holds every capture.
    private void Publish(int slot, object service, Type serviceType)
    {
        var storage = _storage;
        if (storage is null && slot <= MaxSingleSlot && service.GetType() == serviceType && !IsPageStorage(service))
        {
            _layout = (_layout & CapacityMask) | ((slot + 1) << CapacityBits);
            Volatile.Write(ref _storage, service);
            return;
        }
        var structure = storage;
        if (storage is not null && !IsPageStorage(storage))
        {
            structure = Place(null, (_layout >> CapacityBits) - 1, storage);
            _layout &= CapacityMask;
        }
        structure = Place(structure, slot, service);
        if (!ReferenceEquals(structure, storage)) Volatile.Write(ref _storage, structure);
    }

    private object Place(object? structure, int slot, object service)
    {
        var pageIndex = slot >> PageBits;
        var offset = slot & (PageSize - 1);
        object?[]?[] pages;
        if (structure is null || structure.GetType() == typeof(object[]))
        {
            // Page zero is stored directly until a later page needs a jagged directory.
            var page = structure is null ? [] : Unsafe.As<object?[]>(structure);
            if (pageIndex == 0)
            {
                if (offset >= page.Length)
                {
                    var expanded = new object?[page.Length == 0 ? Math.Max(_layout & CapacityMask, offset + 1) : PageSize];
                    Array.Copy(page, expanded, page.Length);
                    page = expanded;
                }
                Volatile.Write(ref page[offset], service);
                return page;
            }
            pages = new object?[]?[Math.Max(pageIndex + 1, page.Length == 0 ? 0 : 2)];
            if (page.Length != 0)
            {
                // Promoted pages retain the full-page invariant used by readers.
                if (page.Length < PageSize)
                {
                    var full = new object?[PageSize];
                    Array.Copy(page, full, page.Length);
                    page = full;
                }
                pages[0] = page;
            }
        }
        else
        {
            pages = Unsafe.As<object?[]?[]>(structure);
            if (pageIndex >= pages.Length)
            {
                var expanded = new object?[]?[Math.Max(pageIndex + 1, pages.Length * 2)];
                Array.Copy(pages, expanded, pages.Length);
                pages = expanded;
            }
        }
        if (pages[pageIndex] is not { } target)
        {
            target = new object?[PageSize];
            Volatile.Write(ref pages[pageIndex], target);
        }
        Volatile.Write(ref target[offset], service);
        return pages;
    }

    // Reentrant so a synchronous factory can dispatch to a different route.
    private bool EnterInitialization()
    {
        var thread = Environment.CurrentManagedThreadId;
        if (Volatile.Read(ref _initializingThread) == thread) return false;
        if (Interlocked.CompareExchange(ref _initializingThread, thread, 0) != 0) WaitForInitialization(thread);
        return true;
    }

    private void WaitForInitialization(int thread)
    {
        lock (InitializationMonitor)
        {
            Interlocked.Increment(ref _initializationWaiters);
            try
            {
                // The owner releases without a fence; this makes its release or our registration visible to the other side.
                Interlocked.MemoryBarrierProcessWide();
                while (Interlocked.CompareExchange(ref _initializingThread, thread, 0) != 0)
                    Monitor.Wait(InitializationMonitor);
            }
            finally
            {
                Interlocked.Decrement(ref _initializationWaiters);
            }
        }
    }

    private void ExitInitialization()
    {
        Volatile.Write(ref _initializingThread, 0);
        if (Volatile.Read(ref _initializationWaiters) != 0)
        {
            lock (InitializationMonitor) Monitor.PulseAll(InitializationMonitor);
        }
    }
}
