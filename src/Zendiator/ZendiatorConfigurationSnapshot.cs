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
        List<(string MarkerType, string Assembly)> assemblyMarkers,
        List<string> assemblies,
        List<(string BehaviorType, int Order)> behaviors,
        List<string> notifications,
        List<(string HandlerType, int Order)> handlerOrders)
    {
        Namespace = @namespace;
        ServiceLifetime = serviceLifetime;
        AssemblyMarkers = assemblyMarkers;
        Assemblies = assemblies;
        Behaviors = behaviors;
        Notifications = notifications;
        HandlerOrders = handlerOrders;
    }

    /// <summary>Gets the generation namespace, or null for the default.</summary>
    public string? Namespace { get; }

    /// <summary>Gets the registration lifetime.</summary>
    public ServiceLifetime ServiceLifetime { get; }

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
        return string.Join(";", parts);
    }
}
