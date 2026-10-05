using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zendiator.DependencyInjection;

/// <summary>Uses a separate service-slot namespace for one generated mediator composition.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class ZendiatorServiceResolver<TMediator> : ZendiatorServiceResolver
{
    private static int _nextSlot;

    private static class ServiceSlot<T>
    {
        internal static readonly int Index = Interlocked.Increment(ref _nextSlot) - 1;
    }

    /// <summary>Creates a dependency cache for a mediator bound to the supplied scope.</summary>
    public ZendiatorServiceResolver(IServiceProvider provider) : base(provider) { }

    /// <summary>Creates a cache with a bounded initial page for a generated composition.</summary>
    public ZendiatorServiceResolver(IServiceProvider provider, int initialPageCapacity) : base(provider, initialPageCapacity) { }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override T GetRequiredService<T>() => GetSharedServiceAtSlot<T>(ServiceSlot<T>.Index);

    /// <summary>Gets a dependency of a generated route that is not kept in a route dependency set.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetDispatchService<T>() where T : notnull => GetRequiredServiceAtSlot<T>(ServiceSlot<T>.Index);

    /// <summary>Gets a generated route dependency set, or null before the route first uses it.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TSet? GetDependencySet<TSet>() where TSet : class => GetDependencySetAtSlot<TSet>(ServiceSlot<TSet>.Index);

    /// <summary>Publishes a generated route dependency set and returns the set that the route must use.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TSet AddDependencySet<TSet>(TSet dependencies) where TSet : class => AddDependencySetAtSlot(ServiceSlot<TSet>.Index, dependencies);

    /// <summary>Publishes a generated route dependency set and captures the dependency of the route's first node.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TSet AddDependencySet<TSet>(TSet dependencies, Action<TSet> captureFirst) where TSet : class
        => AddDependencySetAtSlot(ServiceSlot<TSet>.Index, dependencies, captureFirst);
}
