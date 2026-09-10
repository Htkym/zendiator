using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed class DiSetting
{
    public string? Namespace;
    public Location? NamespaceLocation;
    public List<(INamedTypeSymbol Type, string Key)> Markers { get; } = new();
    public List<string> Assemblies { get; } = new();
    public List<(INamedTypeSymbol Type, bool IsOpen, int Order, string Key)> Behaviors { get; } = new();
    public List<(INamedTypeSymbol Type, string Key)> Notifications { get; } = new();
    public List<(INamedTypeSymbol Type, string Key, int Order)> HandlerOrders { get; } = new();
    public Location CallLocation = Location.None;

    public string StructureKey()
    {
        // Assemblies normalize to one identity set regardless of the spelling
        // (marker type vs typeof(X).Assembly), matching discovery semantics.
        var assemblies = Markers.Select(static entry => entry.Type.ContainingAssembly.Identity.ToString()).Concat(Assemblies)
            .Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal).ToList();
        var parts = new List<string>
        {
            "ns=" + (Namespace ?? ""),
            "assemblies=" + string.Join(",", assemblies),
            "behaviors=" + string.Join(",", Behaviors.OrderBy(static e => e.Order).ThenBy(static e => e.Key, StringComparer.Ordinal).Select(static e => e.Order + ":" + e.Key)),
            "notifications=" + string.Join(",", Notifications.OrderBy(static e => e.Key, StringComparer.Ordinal).Select(static e => e.Key)),
            "orders=" + string.Join(",", HandlerOrders.OrderBy(static e => e.Key, StringComparer.Ordinal).Select(static e => e.Key + ":" + e.Order)),            };
        return string.Join(";", parts);
    }
}
