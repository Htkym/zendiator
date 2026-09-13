using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static void Register(StringBuilder b, string arguments, string lifetime)
    {
        b.AppendLine($$"""
                    global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Describe({{arguments}}, {{lifetime}}));
            """);
    }

    private static void EmitRegistrationEntries(
        StringBuilder b,
        string prefix,
        string mediatorName,
        List<Route> routes,
        List<NotificationRoute> notifications,
        List<MultiRoute> multiRoutes,
        List<Route> syncRoutes,
        List<MultiRoute> syncMultiRoutes,
        List<Route> streamRoutes,
        string lifetime)
    {
        var mediatorNameLocal = mediatorName;
        b.AppendLine("""
                    global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<global::Zendiator.DependencyInjection.ZendiatorRootLifetime>(services);
            """);
        Register(b, $$"""typeof({{mediatorNameLocal}}), typeof({{mediatorNameLocal}})""", lifetime);
        b.AppendLine($$"""
                    global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Describe(typeof({{prefix}}IZendiator), static provider => (object)provider.GetRequiredService<{{mediatorName}}>(), {{lifetime}}));
            """);
        foreach (var service in routes.SelectMany(r => r.Behaviors.Concat(new[] { r.Handler }).Select(s => (Route: r, Service: s))).Select(p => p.Route.IsOpen ? $$"""typeof({{OpenTypeofName(p.Service)}}), typeof({{OpenTypeofName(p.Service)}})""" : $$"""typeof({{Name(p.Service)}}), typeof({{Name(p.Service)}})""").Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
        var notificationServices = new List<string>();
        foreach (var notification in notifications)
        {
            if (notification.IsOpen)
            {
                foreach (var sub in notification.Subscribers)
                    notificationServices.Add($$"""typeof({{OpenTypeofName(sub.Handler)}}), typeof({{OpenTypeofName(sub.Handler)}})""");
            }
            else
            {
                foreach (var sub in notification.Subscribers)
                    notificationServices.Add($$"""typeof({{Name(sub.Handler)}}), typeof({{Name(sub.Handler)}})""");
            }
        }

        foreach (var service in notificationServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
        var multiServices = new List<string>();
        foreach (var multi in multiRoutes)
        {
            foreach (var branch in multi.Branches)
            {
                multiServices.Add(branch.HandlerIsOpen ? $$"""typeof({{OpenTypeofName(branch.Handler)}}), typeof({{OpenTypeofName(branch.Handler)}})""" : $$"""typeof({{Name(branch.Handler)}}), typeof({{Name(branch.Handler)}})""");
                foreach (var behavior in branch.Behaviors)
                {
                    multiServices.Add(multi.IsOpen ? $$"""typeof({{OpenTypeofName(behavior)}}), typeof({{OpenTypeofName(behavior)}})""" : $$"""typeof({{Name(behavior)}}), typeof({{Name(behavior)}})""");
                }
            }
        }

        foreach (var service in multiServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
        var syncServices = new List<string>();
        foreach (var route in syncRoutes)
        {
            foreach (var behavior in route.Behaviors)
            {
                syncServices.Add(route.IsOpen ? $$"""typeof({{OpenTypeofName(behavior)}}), typeof({{OpenTypeofName(behavior)}})""" : $$"""typeof({{Name(behavior)}}), typeof({{Name(behavior)}})""");
            }

            syncServices.Add(route.IsOpen ? $$"""typeof({{OpenTypeofName(route.Handler)}}), typeof({{OpenTypeofName(route.Handler)}})""" : $$"""typeof({{Name(route.Handler)}}), typeof({{Name(route.Handler)}})""");
        }

        foreach (var multi in syncMultiRoutes)
        {
            foreach (var branch in multi.Branches)
            {
                syncServices.Add(branch.HandlerIsOpen ? $$"""typeof({{OpenTypeofName(branch.Handler)}}), typeof({{OpenTypeofName(branch.Handler)}})""" : $$"""typeof({{Name(branch.Handler)}}), typeof({{Name(branch.Handler)}})""");
                foreach (var behavior in branch.Behaviors)
                {
                    syncServices.Add(multi.IsOpen ? $$"""typeof({{OpenTypeofName(behavior)}}), typeof({{OpenTypeofName(behavior)}})""" : $$"""typeof({{Name(behavior)}}), typeof({{Name(behavior)}})""");
                }
            }
        }

        foreach (var service in syncServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
        var streamServices = new List<string>();
        foreach (var route in streamRoutes)
        {
            foreach (var behavior in route.Behaviors)
            {
                streamServices.Add(route.IsOpen ? $$"""typeof({{OpenTypeofName(behavior)}}), typeof({{OpenTypeofName(behavior)}})""" : $$"""typeof({{Name(behavior)}}), typeof({{Name(behavior)}})""");
            }

            streamServices.Add(route.IsOpen ? $$"""typeof({{OpenTypeofName(route.Handler)}}), typeof({{OpenTypeofName(route.Handler)}})""" : $$"""typeof({{Name(route.Handler)}}), typeof({{Name(route.Handler)}})""");
        }

        foreach (var service in streamServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, lifetime);
    }

    private static void EmitRegistrar(
        StringBuilder b,
        string prefix,
        string mediatorName,
        List<Route> routes,
        List<NotificationRoute> notifications,
        List<MultiRoute> multiRoutes,
        List<Route> syncRoutes,
        List<MultiRoute> syncMultiRoutes,
        List<Route> streamRoutes,
        string fingerprint)
    {
        b.AppendLine($$"""
            /// <summary>Generated DI registrar. Prefer AddZendiator; do not call directly.</summary>
            internal static class ZendiatorGeneratedRegistrar
            {
                internal const string StructureFingerprint = "{{EscapeString(fingerprint)}}";
                internal static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Zendiator.DependencyInjection.ZendiatorConfigurationSnapshot snapshot)
                {
                    global::System.ArgumentNullException.ThrowIfNull(services);
                    global::System.ArgumentNullException.ThrowIfNull(snapshot);
                    if (snapshot.GetFingerprint() != StructureFingerprint)
                        throw new global::System.InvalidOperationException("AddZendiator configuration does not match the generated structure. Keep one configuration per compilation.");
            """);
        EmitRegistrationEntries(
            b,
            prefix,
            mediatorName,
            routes,
            notifications,
            multiRoutes,
            syncRoutes,
            syncMultiRoutes,
            streamRoutes,
            "snapshot.ServiceLifetime");
        b.AppendLine("""
                    return services;
                }
            }
            """);
    }

    private static void EmitRegistration(
        StringBuilder b,
        string prefix,
        string mediatorName,
        List<Route> routes,
        List<NotificationRoute> notifications,
        List<MultiRoute> multiRoutes,
        List<Route> syncRoutes,
        List<MultiRoute> syncMultiRoutes,
        List<Route> streamRoutes,
        DiEmitOptions? di)
    {
        if (di == null || di.EmitExtensions)
        {
            b.AppendLine("""
                /// <summary>Registers the generated mediator and its concrete services.</summary>
                public static class ZendiatorServiceCollectionExtensions
                {
                    /// <summary>Adds scoped defaults without replacing existing registrations.</summary>
                    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZendiator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    {
                        global::System.ArgumentNullException.ThrowIfNull(services);
                        return AddZendiator(services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped);
                    }
                    /// <summary>Adds defaults with the selected lifetime without replacing existing registrations. All registrations share the lifetime.</summary>
                    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZendiator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime)
                    {
                        global::System.ArgumentNullException.ThrowIfNull(services);
                        if (lifetime is not (global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton or global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped or global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient))
                            throw new global::System.ArgumentOutOfRangeException(nameof(lifetime), lifetime, null);
                """);
            EmitRegistrationEntries(
                b,
                prefix,
                mediatorName,
                routes,
                notifications,
                multiRoutes,
                syncRoutes,
                syncMultiRoutes,
                streamRoutes,
                "lifetime");
            b.AppendLine("""
                        return services;
                    }
                }
                """);
        }
        else
        {
            EmitRegistrar(
                b,
                prefix,
                mediatorName,
                routes,
                notifications,
                multiRoutes,
                syncRoutes,
                syncMultiRoutes,
                streamRoutes,
                di.Fingerprint);
        }
    }
}
