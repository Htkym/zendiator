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

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sealed override T GetRequiredService<T>() => GetRequiredServiceAtSlot<T>(ServiceSlot<T>.Index);
}
