using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DependencyInjection;

/// <summary>
/// DI-based generation settings. The generator analyzes supported calls on this
/// object; anything else is diagnosed. Instances are single-use: recording ends
/// when <see cref="Snapshot"/> is called.
/// </summary>
public sealed class ZendiatorConfiguration
{
    private bool _frozen;
    private string? _namespace;
    private ServiceLifetime _serviceLifetime = ServiceLifetime.Scoped;
    private readonly List<string> _assemblyMarkers = new();
    private readonly List<string> _assemblyMarkerAssemblies = new();
    private readonly List<string> _assemblies = new();
    private readonly List<(string BehaviorType, int Order)> _behaviors = new();
    private readonly List<string> _notifications = new();
    private readonly List<(string HandlerType, int Order)> _handlerOrders = new();

    /// <summary>Gets or sets the generation namespace. Null selects the default.</summary>
    public string? Namespace
    {
        get => _namespace;
        set
        {
            ThrowIfFrozen();
            _namespace = value;
        }
    }

    /// <summary>Gets or sets the registration lifetime. Scoped by default. This value flows at runtime and never changes the generated structure.</summary>
    public ServiceLifetime ServiceLifetime
    {
        get => _serviceLifetime;
        set
        {
            ThrowIfFrozen();
            _serviceLifetime = value;
        }
    }

    /// <summary>Includes the assembly containing the marker type in discovery.</summary>
    public void RegisterServicesFromAssemblyContaining<TMarker>()
    {
        ThrowIfFrozen();
        _assemblyMarkers.Add(TypeName(typeof(TMarker)));
        _assemblyMarkerAssemblies.Add(typeof(TMarker).Assembly.FullName ?? typeof(TMarker).Assembly.GetName().Name ?? "");
    }

    /// <summary>Includes an assembly in discovery. Generation analysis only supports <c>typeof(X).Assembly</c> arguments.</summary>
    public void RegisterServicesFromAssembly(Assembly assembly)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly.FullName ?? assembly.GetName().Name ?? "");
    }

    /// <summary>Adds a closed behavior or an open generic behavior with an explicit order.</summary>
    public void AddOpenBehavior(Type behaviorType, int order)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(behaviorType);
        _behaviors.Add((TypeName(behaviorType), order));
    }

    /// <summary>Adds a closed stream behavior or an open generic stream behavior with an explicit order. MediatR migration alias with identical semantics to <see cref="AddOpenBehavior"/> for stream contracts.</summary>
    public void AddOpenStreamBehavior(Type behaviorType, int order)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(behaviorType);
        _behaviors.Add((TypeName(behaviorType), order));
    }

    /// <summary>Declares a known notification type, including subscriber-less and closed generic targets.</summary>
    public void AddNotification<TNotification>()
    {
        ThrowIfFrozen();
        _notifications.Add(TypeName(typeof(TNotification)));
    }

    /// <summary>Sets the dispatch order of a notification subscriber or a multi-request handler.</summary>
    public void ConfigureHandlerOrder(Type handlerType, int order)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(handlerType);
        _handlerOrders.Add((TypeName(handlerType), order));
    }

    /// <summary>Freezes this instance and returns a read-only snapshot. Single-use: a second call throws.</summary>
    public ZendiatorConfigurationSnapshot Snapshot()
    {
        if (_frozen)
            throw new InvalidOperationException("This ZendiatorConfiguration has already been used. Configurations are single-use per registration call.");
        Validate();
        _frozen = true;
        return new ZendiatorConfigurationSnapshot(
            _namespace,
            _serviceLifetime,
            _assemblyMarkers
                .Select((marker, index) => (MarkerType: marker, Assembly: _assemblyMarkerAssemblies[index]))
                .OrderBy(static entry => entry.MarkerType, StringComparer.Ordinal).ToList(),
            _assemblies.OrderBy(static name => name, StringComparer.Ordinal).ToList(),
            _behaviors.OrderBy(static entry => entry.Order).ThenBy(static entry => entry.BehaviorType, StringComparer.Ordinal).ToList(),
            _notifications.OrderBy(static name => name, StringComparer.Ordinal).ToList(),
            _handlerOrders.OrderBy(static entry => entry.HandlerType, StringComparer.Ordinal).ToList());
    }

    private static string TypeName(Type type) => type.IsGenericTypeDefinition ? type.FullName ?? type.Name : type.ToString();

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("This ZendiatorConfiguration has already been used. Configurations are single-use per registration call.");
    }

    private void Validate()
    {
        var duplicateBehavior = _behaviors.GroupBy(static entry => entry.BehaviorType, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() != 1);
        if (duplicateBehavior != null)
            throw new InvalidOperationException($"Behavior {duplicateBehavior.Key} is registered more than once. Register each behavior type once.");
        var duplicateOrder = _behaviors.GroupBy(static entry => entry.Order).FirstOrDefault(static group => group.Count() != 1);
        if (duplicateOrder != null)
            throw new InvalidOperationException($"Behavior order {duplicateOrder.Key} is used more than once. Order values must be unique.");
        var duplicateNotification = _notifications.GroupBy(static name => name, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() != 1);
        if (duplicateNotification != null)
            throw new InvalidOperationException($"Notification {duplicateNotification.Key} is declared more than once.");
        var duplicateHandlerOrder = _handlerOrders.GroupBy(static entry => entry.HandlerType, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() != 1);
        if (duplicateHandlerOrder != null)
            throw new InvalidOperationException($"Handler {duplicateHandlerOrder.Key} has more than one configured order.");
    }
}
