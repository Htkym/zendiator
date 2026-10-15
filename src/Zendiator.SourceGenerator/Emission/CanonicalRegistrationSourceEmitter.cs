using System;
using System.Linq;
using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalRegistrationSourceEmitter
{
    internal static void Emit(StringBuilder b, CanonicalRegistrationPlan plan)
    {
        var c = plan.Composition;
        b.AppendLine("""
                internal static class ZendiatorGeneratedRegistrar
                {
                    internal static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add(
                        global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                        global::Zendiator.DependencyInjection.ZendiatorConfigurationSnapshot snapshot)
                    {
                        global::System.ArgumentNullException.ThrowIfNull(services);
                        global::System.ArgumentNullException.ThrowIfNull(snapshot);
                        var expected = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
            """);
        if (c.UseLocalHandlers) b.AppendLine("            expected.UseLocalHandlers();");
        foreach (var marker in c.CatalogMarkerTypes) b.AppendLine("            expected.UseCatalogFrom<" + marker + ">();");
        foreach (var type in c.ExplicitHandlerTypes) b.AppendLine("            expected.AddHandler(typeof(" + type + "));");
        foreach (var route in plan.Routes.OrderBy(r => r.HandlerOrdinal)) Handler(b, route);
        foreach (var route in plan.Routes.OrderBy(r => r.PipelineOrdinal)) Pipeline(b, route);
        b.AppendLine("""
                        if (snapshot.GetFingerprint() != expected.Snapshot().GetFingerprint())
                            throw new global::System.InvalidOperationException("AddZendiator configuration does not match this generated composition.");
            """);
        foreach (var type in plan.Routes.SelectMany(r => r.ServiceTypes).Concat(new[] { "global::" + c.Namespace + ".Zendiator" }).Distinct(StringComparer.Ordinal))
            b.AppendLine($$"""
                        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services,
                            global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof({{type}}), typeof({{type}})));
                """);
        b.AppendLine($$"""
                        global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services,
                            global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof(global::{{c.Namespace}}.IZendiator),
                                provider => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::{{c.Namespace}}.Zendiator>(provider)));
                        return services;
                    }
                }
            """);
    }

    private static string Stages(EquatableArray<RegistrationStageEmission> stages) => string.Join(", ", stages.Select(s =>
        "global::Zendiator.Stage." + s.Kind + "(typeof(" + s.TypeName + ")" + (s.FactoryName == null ? "" : ", typeof(" + s.FactoryName + ")") + ")"));

    private static void Handler(StringBuilder b, CanonicalRouteRegistration route)
    {
        var c = route.Registration;
        if (route.Open is { } open)
        {
            var entry = open.HandlerDefinitionNames.Count != 0 ? string.Join(", ", open.HandlerDefinitionNames.Select(name => "typeof(" + name + ")")) :
                open.HandlerFactoryDefinitionName == null ? "typeof(" + open.HandlerDefinitionName + ")" :
                "global::Zendiator.HandlerEntry.Session(typeof(" + open.HandlerDefinitionName + "), typeof(" + open.HandlerFactoryDefinitionName + "))";
            b.AppendLine($$"""            expected.Handlers(typeof({{open.RequestDefinitionName}}), global::Zendiator.DispatchFamily.{{route.Family}}, {{entry}});""");
        }
        else if (c.Handler.RequestIsRefLike)
            b.AppendLine($$"""            expected.Handlers(typeof({{c.Handler.RequestName}}), global::Zendiator.DispatchFamily.{{route.Family}}, typeof({{c.Handler.HandlerName}}));""");
        else if (route.BranchHandlers.Count != 0)
        {
            var entries = string.Join(", ", route.BranchHandlers.Select(h => h.FactoryName == null ? "typeof(" + h.HandlerName + ")" : "global::Zendiator.HandlerEntry.Session<" + h.HandlerName + ", " + h.FactoryName + ">()"));
            b.AppendLine($$"""            expected.Handlers<{{c.Handler.RequestName}}>(global::Zendiator.DispatchFamily.{{route.Family}}, {{entries}});""");
        }
        else
        {
            var entry = c.Factory == null ? "typeof(" + c.Handler.HandlerName + ")" : "global::Zendiator.HandlerEntry.Session<" + c.Handler.HandlerName + ", " + c.Factory.FactoryName + ">()";
            b.AppendLine($$"""            expected.Handlers<{{c.Handler.RequestName}}>(global::Zendiator.DispatchFamily.{{route.Family}}, {{entry}});""");
        }
    }

    private static void Pipeline(StringBuilder b, CanonicalRouteRegistration route)
    {
        var c = route.Registration;
        if (route.Open is { } open)
        {
            if (open.FinalDefinitionName != null)
                b.AppendLine($$"""            expected.StreamPipeline(typeof({{open.RequestDefinitionName}}), typeof({{open.FinalDefinitionName}}), {{Stages(open.Stages)}});""");
            else
                b.AppendLine($$"""            expected.Pipeline(typeof({{open.RequestDefinitionName}}), {{(c.PipelineFamily == null ? "" : "global::Zendiator.DispatchFamily." + route.Family + ", ")}}{{Stages(open.Stages)}});""");
        }
        else if (c.Handler.RequestIsRefLike)
            b.AppendLine($$"""            expected.Pipeline(typeof({{c.Handler.RequestName}}), global::Zendiator.DispatchFamily.{{route.Family}}, {{Stages(c.RegistrationStages)}});""");
        else if (route.FinalItemName != null)
            b.AppendLine($$"""            expected.StreamPipeline<{{c.Handler.RequestName}}, {{route.FinalItemName}}>({{Stages(route.DeclaredStages)}});""");
        else if (c.RegistrationStages.Count != 0)
            b.AppendLine($$"""            expected.Pipeline<{{c.Handler.RequestName}}>({{(c.PipelineFamily == null ? "" : "global::Zendiator.DispatchFamily." + route.Family + ", ")}}{{string.Join(", ", c.RegistrationStages.Select(s => "global::Zendiator.Stage." + s.Kind + "<" + s.TypeName + (s.FactoryName == null ? "" : ", " + s.FactoryName) + ">()"))}});""");
        else if (c.Hooks.Count != 0)
            b.AppendLine($$"""            expected.Pipeline<{{c.Handler.RequestName}}>({{(c.PipelineFamily == null ? "" : "global::Zendiator.DispatchFamily." + route.Family + ", ")}}{{string.Join(", ", c.Hooks.Select(h => h.Factory == null ? "global::Zendiator.Stage.Hook<" + h.HookName + ">()" : "global::Zendiator.Stage.Hook<" + h.HookName + ", " + h.Factory.FactoryName + ">()"))}});""");
        else if (c.HasEmptyPipeline)
            b.AppendLine($$"""            expected.Pipeline<{{c.Handler.RequestName}}>({{(c.PipelineFamily == null ? "" : "global::Zendiator.DispatchFamily." + route.Family)}});""");
    }
}
