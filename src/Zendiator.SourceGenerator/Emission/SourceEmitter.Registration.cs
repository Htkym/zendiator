using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private static void Register(StringBuilder b, string arguments, string lifetime)
    {
        b.AppendLine($$"""
                    global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Describe({{arguments}}, {{lifetime}}));
            """);
    }

    private void EmitRegistrationEntries(StringBuilder b, string lifetime, string dependencyLifetime)
    {
        var mediatorNameLocal = _model.Target.MediatorName;
        Register(b, $$"""typeof({{_model.Target.Prefix}}IZendiator), typeof({{mediatorNameLocal}})""", lifetime);
        b.AppendLine($$"""            var serviceLifetime = {{dependencyLifetime}};""");
        foreach (var service in _model.Routes.Requests.Single.SelectMany(r => r.Behaviors.Concat(new[] { r.Handler }).Select(s => (Route: r, Service: s))).Select(p => p.Route.IsOpen ? $$"""typeof({{OpenTypeofName(p.Service)}}), typeof({{OpenTypeofName(p.Service)}})""" : $$"""typeof({{Name(p.Service)}}), typeof({{Name(p.Service)}})""").Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, "serviceLifetime");
        var notificationServices = new List<string>();
        foreach (var notification in _model.Routes.Notifications)
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
            Register(b, service, "serviceLifetime");
        var multiServices = new List<string>();
        foreach (var multi in _model.Routes.Requests.Multiple)
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
            Register(b, service, "serviceLifetime");
        var syncServices = new List<string>();
        foreach (var route in _model.Routes.Synchronous.Single)
        {
            foreach (var behavior in route.Behaviors)
            {
                syncServices.Add(route.IsOpen ? $$"""typeof({{OpenTypeofName(behavior)}}), typeof({{OpenTypeofName(behavior)}})""" : $$"""typeof({{Name(behavior)}}), typeof({{Name(behavior)}})""");
            }

            syncServices.Add(route.IsOpen ? $$"""typeof({{OpenTypeofName(route.Handler)}}), typeof({{OpenTypeofName(route.Handler)}})""" : $$"""typeof({{Name(route.Handler)}}), typeof({{Name(route.Handler)}})""");
        }

        foreach (var multi in _model.Routes.Synchronous.Multiple)
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
            Register(b, service, "serviceLifetime");
        var streamServices = new List<string>();
        foreach (var route in _model.Routes.Streams)
        {
            foreach (var behavior in route.Behaviors)
            {
                streamServices.Add(route.IsOpen ? $$"""typeof({{OpenTypeofName(behavior)}}), typeof({{OpenTypeofName(behavior)}})""" : $$"""typeof({{Name(behavior)}}), typeof({{Name(behavior)}})""");
            }

            streamServices.Add(route.IsOpen ? $$"""typeof({{OpenTypeofName(route.Handler)}}), typeof({{OpenTypeofName(route.Handler)}})""" : $$"""typeof({{Name(route.Handler)}}), typeof({{Name(route.Handler)}})""");
        }

        foreach (var service in streamServices.Distinct().OrderBy(n => n, StringComparer.Ordinal))
            Register(b, service, "serviceLifetime");
    }

    private void EmitRegistrar(StringBuilder b)
    {
        b.AppendLine($$"""
            /// <summary>Generated DI registrar. Prefer AddZendiator; do not call directly.</summary>
            internal static class ZendiatorGeneratedRegistrar
            {
                internal const string StructureFingerprint = "{{EscapeString(_model.Target.ConfigurationFingerprint!)}}";
                internal static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Zendiator.DependencyInjection.ZendiatorConfigurationSnapshot snapshot)
                {
                    global::System.ArgumentNullException.ThrowIfNull(services);
                    global::System.ArgumentNullException.ThrowIfNull(snapshot);
                    if (snapshot.GetFingerprint() != StructureFingerprint)
                        throw new global::System.InvalidOperationException("AddZendiator configuration does not match the generated structure. Keep one configuration per compilation.");
            """);
        EmitRegistrationEntries(b, "snapshot.ServiceLifetime",
            "snapshot.DependencyLifetime ?? (snapshot.ServiceLifetime == global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped ? global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient : snapshot.ServiceLifetime)");
        b.AppendLine("""
                    return services;
                }
            }
            """);
    }

    private void EmitRegistration(StringBuilder b)
    {
        if (_model.Target.EmitExtensions)
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
                    /// <summary>Adds defaults with the selected mediator lifetime without replacing existing registrations.</summary>
                    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZendiator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime)
                    {
                        return AddZendiator(services, lifetime, lifetime == global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped
                            ? global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient : lifetime);
                    }
                    /// <summary>Adds defaults with separate mediator and dependency lifetimes without replacing existing registrations.</summary>
                    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddZendiator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime dependencyLifetime)
                    {
                        global::System.ArgumentNullException.ThrowIfNull(services);
                        if (lifetime is not (global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton or global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped or global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient))
                            throw new global::System.ArgumentOutOfRangeException(nameof(lifetime), lifetime, null);
                        if (dependencyLifetime is not (global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton or global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped or global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient))
                            throw new global::System.ArgumentOutOfRangeException(nameof(dependencyLifetime), dependencyLifetime, null);
                """);
            EmitRegistrationEntries(b, "lifetime", "dependencyLifetime");
            b.AppendLine("""
                        return services;
                    }
                }
                """);
        }
        else
        {
            EmitRegistrar(b);
        }
    }
}
