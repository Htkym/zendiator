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
    private ServiceLifetime? _dependencyLifetime;
    private readonly List<string> _assemblyMarkers = new();
    private readonly List<string> _assemblyMarkerAssemblies = new();
    private readonly List<string> _assemblies = new();
    private List<(string BehaviorType, int Order)> _behaviors = new();
    private readonly List<string> _notifications = new();
    private readonly List<(string HandlerType, int Order)> _handlerOrders = new();
    private bool _useLocalHandlers;
    private readonly List<Type> _handlers = new();
    private readonly List<Type> _catalogMarkers = new();
    private readonly List<(Type Request, DispatchFamily? Family, IReadOnlyList<Stage> Stages)> _pipelines = new();
    private readonly List<(Type Request, DispatchFamily Family, IReadOnlyList<HandlerEntry> Handlers)> _routeHandlers = new();

    /// <summary>Declares stages in order, inferring an unambiguous dispatch family from selected handlers.</summary>
    public void Pipeline<TRequest>(params Stage[] stages) => RecordPipeline(typeof(TRequest), null, stages);
    /// <summary>Declares stages in order for one explicit dispatch family.</summary>
    public void Pipeline<TRequest>(DispatchFamily family, params Stage[] stages) => RecordPipeline(typeof(TRequest), family, stages);
    /// <summary>Declares ordered stages for a known closed request or an open request definition.</summary>
    public void Pipeline(Type request, DispatchFamily family, params Stage[] stages)
    {
        ArgumentNullException.ThrowIfNull(request);
        RecordPipeline(request, family, stages);
    }
    /// <summary>Declares the handler list for a known closed request or an open request definition.</summary>
    public void Handlers(Type request, DispatchFamily family, params Type[] handlers)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handlers);
        if (handlers.Any(static type => type == null)) throw new ArgumentException("Every handler entry must name a type.", nameof(handlers));
        RecordHandlers(request, family, handlers.Select(static type => HandlerEntry.Service(type)).ToArray());
    }
    /// <summary>Declares the authoritative handler order for one route.</summary>
    public void Handlers<TRequest>(DispatchFamily family, params Type[] handlers)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(handlers);
        if (handlers.Any(static type => type == null)) throw new ArgumentException("Every handler entry must name a type.", nameof(handlers));
        RecordHandlers(typeof(TRequest), family, handlers.Select(static type => HandlerEntry.Service(type)).ToArray());
    }

    /// <summary>Declares an authoritative list containing explicit handler sessions and borrowed service entries.</summary>
    public void Handlers<TRequest>(DispatchFamily family, HandlerEntry first, params HandlerEntry[] rest)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(rest);
        RecordHandlers(typeof(TRequest), family, new[] { first }.Concat(rest).ToArray());
    }

    private void RecordHandlers(Type request, DispatchFamily family, HandlerEntry[] handlers)
    {
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        if (handlers.Any(static entry => entry.Type == null)) throw new ArgumentException("Every handler entry must name a type.", nameof(handlers));
        _routeHandlers.Add((request, family, Array.AsReadOnly(handlers.ToArray())));
    }

    private void RecordPipeline(Type request, DispatchFamily? family, Stage[] stages)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(stages);
        if (family is { } value && !Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(family));
        if (stages.Any(static stage => stage.Type == null)) throw new ArgumentException("Every stage entry must name a type.", nameof(stages));
        _pipelines.Add((request, family, Array.AsReadOnly(stages.ToArray())));
    }

    /// <summary>Selects marked handlers from the calling compilation for a 0.5 composition.</summary>
    public void UseLocalHandlers()
    {
        ThrowIfFrozen();
        _useLocalHandlers = true;
    }

    /// <summary>Selects the generated compile-time Handler catalog of the marker type\'s assembly.</summary>
    public void UseCatalogFrom<TMarker>()
    {
        ThrowIfFrozen();
        var marker=typeof(TMarker);
        if(_catalogMarkers.Any(type=>type.Assembly==marker.Assembly))throw new InvalidOperationException("The same catalog is selected more than once.");
        _catalogMarkers.Add(marker);
    }

    /// <summary>Selects a known handler type for a 0.5 composition.</summary>
    public void AddHandler<THandler>() => AddHandler(typeof(THandler));

    /// <summary>Selects a known closed handler or an open generic handler definition for a 0.5 composition.</summary>
    public void AddHandler(Type handlerType)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(handlerType);
        if (_handlers.Contains(handlerType))
            throw new InvalidOperationException($"Handler {TypeName(handlerType)} is selected more than once.");
        _handlers.Add(handlerType);
    }

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

    /// <summary>Gets or sets the mediator lifetime. Scoped by default. This value flows at runtime and never changes the generated structure.</summary>
    public ServiceLifetime ServiceLifetime
    {
        get => _serviceLifetime;
        set
        {
            ThrowIfFrozen();
            _serviceLifetime = value;
        }
    }

    /// <summary>Overrides the lifetime of generated Handler, Behavior and Validator registrations. Null uses Transient for a Scoped mediator, or the mediator lifetime otherwise.</summary>
    public ServiceLifetime? DependencyLifetime
    {
        get => _dependencyLifetime;
        set
        {
            ThrowIfFrozen();
            _dependencyLifetime = value;
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

    /// <summary>Adds a closed synchronous stream validator with an explicit, unique validator order.</summary>
    /// <remarks>Validators run at StreamAsync entry. Their captured instances follow the same DI
    /// lifetime and reuse rules as generated handlers and behaviors.</remarks>
    public void AddStreamRequestValidator(Type validatorType, int order)
    {
        ThrowIfFrozen();
        ArgumentNullException.ThrowIfNull(validatorType);
        var name = TypeName(validatorType);
        if (_behaviors is not StreamValidatorRegistrations registrations)
        {
            registrations = new StreamValidatorRegistrations(_behaviors, new());
            _behaviors = registrations;
        }
        if (registrations.Validators.Any(entry => entry.ValidatorType == name))
            throw new InvalidOperationException($"Validator {name} is registered more than once. Register each validator type once.");
        if (registrations.Validators.Any(entry => entry.Order == order))
            throw new InvalidOperationException($"Validator order {order} is used more than once. Validator order values must be unique.");
        registrations.Validators.Add((name, order));
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
            _dependencyLifetime,
            _assemblyMarkers
                .Select((marker, index) => (MarkerType: marker, Assembly: _assemblyMarkerAssemblies[index]))
                .OrderBy(static entry => entry.MarkerType, StringComparer.Ordinal).ToList(),
            _assemblies.OrderBy(static name => name, StringComparer.Ordinal).ToList(),
            _behaviors.OrderBy(static entry => entry.Order).ThenBy(static entry => entry.BehaviorType, StringComparer.Ordinal).ToList(),
            _notifications.OrderBy(static name => name, StringComparer.Ordinal).ToList(),
            _handlerOrders.OrderBy(static entry => entry.HandlerType, StringComparer.Ordinal).ToList(),
            _behaviors is StreamValidatorRegistrations validators
                ? validators.Validators.OrderBy(static entry => entry.Order).ThenBy(static entry => entry.ValidatorType, StringComparer.Ordinal).ToList()
                : null,
            _useLocalHandlers,
            _handlers.Select(static type => type.AssemblyQualifiedName ?? TypeName(type)).OrderBy(static name => name, StringComparer.Ordinal).ToList(),
            _pipelines.ToList(), _routeHandlers.ToList(), _catalogMarkers.ToList());
    }

    private static string TypeName(Type type) => type.IsGenericTypeDefinition ? type.FullName ?? type.Name : type.ToString();

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("This ZendiatorConfiguration has already been used. Configurations are single-use per registration call.");
    }

    private void Validate()
    {
        if (_dependencyLifetime is { } dependencyLifetime &&
            dependencyLifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped or ServiceLifetime.Transient))
            throw new ArgumentOutOfRangeException(nameof(DependencyLifetime), dependencyLifetime, null);
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
