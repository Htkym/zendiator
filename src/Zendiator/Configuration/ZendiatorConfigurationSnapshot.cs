using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DependencyInjection;

/// <summary>
/// Frozen, read-only DI configuration. Generated registrars compare snapshots
/// against the analyzed structure before registering anything.
/// </summary>
public sealed class ZendiatorConfigurationSnapshot
{
    internal ZendiatorConfigurationSnapshot(
        string? @namespace,
        ServiceLifetime serviceLifetime,
        ServiceLifetime? dependencyLifetime,
        List<(string MarkerType, string Assembly)> assemblyMarkers,
        List<string> assemblies,
        List<(string BehaviorType, int Order)> behaviors,
        List<string> notifications,
        List<(string HandlerType, int Order)> handlerOrders,
        bool useLocalHandlers = false,
        List<string>? handlers = null,
        List<(Type Request, DispatchFamily? Family, IReadOnlyList<Stage> Stages)>? pipelines = null,
        List<(Type Request, DispatchFamily Family, IReadOnlyList<HandlerEntry> Handlers)>? routeHandlers = null,
        List<Type>? catalogMarkers = null,
        List<(Type Request, Type Item)>? streamOutputs = null)
    {
        Namespace = @namespace;
        ServiceLifetime = serviceLifetime;
        DependencyLifetime = dependencyLifetime;
        AssemblyMarkers = assemblyMarkers.AsReadOnly();
        Assemblies = assemblies.AsReadOnly();
        Behaviors = behaviors.AsReadOnly();
        Notifications = notifications.AsReadOnly();
        HandlerOrders = handlerOrders.AsReadOnly();
        UseLocalHandlers = useLocalHandlers;
        CatalogMarkers = (catalogMarkers ?? new()).AsReadOnly();
        Handlers = (handlers ?? new()).AsReadOnly();
        Pipelines = (pipelines ?? new()).AsReadOnly();
        RouteHandlers = (routeHandlers ?? new()).AsReadOnly();
        StreamOutputs = (streamOutputs ?? new()).AsReadOnly();
    }

    /// <summary>Gets the generation namespace, or null for the default.</summary>
    public string? Namespace { get; }

    public ServiceLifetime ServiceLifetime { get; }

    /// <summary>Gets the optional Handler, Behavior and Validator registration lifetime override.</summary>
    public ServiceLifetime? DependencyLifetime { get; }

    /// <summary>Gets marker types with their assemblies, sorted by marker type.</summary>
    public IReadOnlyList<(string MarkerType, string Assembly)> AssemblyMarkers { get; }

    /// <summary>Gets the sorted assembly identities from direct assembly registration.</summary>
    public IReadOnlyList<string> Assemblies { get; }

    /// <summary>Gets behaviors ordered by order, then type.</summary>
    public IReadOnlyList<(string BehaviorType, int Order)> Behaviors { get; }

    /// <summary>Gets the sorted notification type names.</summary>
    public IReadOnlyList<string> Notifications { get; }

    /// <summary>Gets handler orders sorted by handler type.</summary>
    public IReadOnlyList<(string HandlerType, int Order)> HandlerOrders { get; }

    /// <summary>Gets whether marked local handlers were selected for a 0.5 composition.</summary>
    public bool UseLocalHandlers { get; }

    /// <summary>Gets marker types selecting generated catalogs. Runtime records assembly identity without reading attributes.</summary>
    public IReadOnlyList<Type> CatalogMarkers { get; }

    /// <summary>Gets explicitly selected handler identities, sorted by identity.</summary>
    public IReadOnlyList<string> Handlers { get; }

    /// <summary>Gets declared pipelines with their original stage order.</summary>
    public IReadOnlyList<(Type Request, DispatchFamily? Family, IReadOnlyList<Stage> Stages)> Pipelines { get; }
    /// <summary>Gets authoritative route handler lists with their original order.</summary>
    public IReadOnlyList<(Type Request, DispatchFamily Family, IReadOnlyList<HandlerEntry> Handlers)> RouteHandlers { get; }
    /// <summary>Gets explicit final item types for type-changing Stream pipelines.</summary>
    public IReadOnlyList<(Type Request, Type Item)> StreamOutputs { get; }

    /// <summary>Returns the normalized structural fingerprint. Equal configurations share one generated unit.</summary>
    public string GetFingerprint()
    {
        // Assemblies normalize to one identity set regardless of the spelling
        // (marker type vs direct assembly), matching discovery semantics.
        var assemblies = AssemblyMarkers.Select(static entry => entry.Assembly).Concat(Assemblies)
            .Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal).ToList();
        var parts = new List<string>
        {
            "ns=" + (Namespace ?? ""),
            "assemblies=" + string.Join(",", assemblies),
            "behaviors=" + string.Join(",", Behaviors.Select(static entry => entry.Order + ":" + entry.BehaviorType)),
            "notifications=" + string.Join(",", Notifications),
            "orders=" + string.Join(",", HandlerOrders.Select(static entry => entry.HandlerType + ":" + entry.Order)),
        };
        if (CatalogMarkers.Count != 0) parts.Add("catalogs=" + string.Join(",", CatalogMarkers.Select(static type => type.Assembly.FullName).OrderBy(static identity => identity, StringComparer.Ordinal)));
        if (UseLocalHandlers || Handlers.Count != 0)
            parts.Add("localHandlers=" + UseLocalHandlers + ";handlers=" + string.Join(",", Handlers));
        if (Pipelines.Count != 0)
            parts.Add("pipelines=" + string.Join("|", Pipelines.Select(static p => p.Request.AssemblyQualifiedName + ":" + p.Family + ":" +
                string.Join(",", p.Stages.Select(static s => s.Kind + ":" + s.Type!.AssemblyQualifiedName +
                    (s.FactoryType == null ? "" : ":factory=" + s.FactoryType.AssemblyQualifiedName))))));
        if (RouteHandlers.Count != 0)
            parts.Add("routeHandlers=" + string.Join("|", RouteHandlers.Select(static p => p.Request.AssemblyQualifiedName + ":" + p.Family + ":" +
                string.Join(",", p.Handlers.Select(static entry => entry.Type!.AssemblyQualifiedName +
                    (entry.FactoryType == null ? "" : ":factory=" + entry.FactoryType.AssemblyQualifiedName))))));
        if (StreamOutputs.Count != 0)
            parts.Add("streamOutputs=" + string.Join("|", StreamOutputs.Select(static p => p.Request.AssemblyQualifiedName + ":" + p.Item.AssemblyQualifiedName)));
        return string.Join(";", parts);
    }
}
