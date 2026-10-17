using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.DependencyInjection;

/// <summary>DI registration entry points. Generation connects supported calls; uninterrupted calls fail fast.</summary>
public static class ZendiatorServiceCollectionExtensions
{
    /// <summary>Adds the explicitly selected handlers for a named 0.5 composition. Requires canonical generation support.</summary>
    public static IServiceCollection AddZendiator<TComposition>(this IServiceCollection services, Action<ZendiatorConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        throw new InvalidOperationException(
            "The 0.5 canonical generation backend is not connected to this composition. Rebuild with canonical generation support.");
    }

    /// <summary>Adds Zendiator with default configuration for the calling compilation.</summary>
    public static IServiceCollection AddZendiator(this IServiceCollection services) =>
        AddZendiator(services, configure: null);

    /// <summary>Adds Zendiator with the supplied configuration for the calling compilation.</summary>
    public static IServiceCollection AddZendiator(this IServiceCollection services, Action<ZendiatorConfiguration>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        throw new InvalidOperationException(
            "Zendiator source generation is not connected to this AddZendiator call. " +
            "Reference the Zendiator package with its source generator enabled, " +
            "call AddZendiator with a supported configuration expression, and rebuild. " +
            "Attribute-based configuration keeps working with its own generated registration.");
    }
}
