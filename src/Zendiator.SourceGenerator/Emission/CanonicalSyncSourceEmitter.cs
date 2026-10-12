using System.Text;

namespace Zendiator.SourceGenerator;

/// <summary>Emits the limited stage-free and single-Hook SendSync matrix accepted by review.</summary>
internal static class CanonicalSyncSourceEmitter
{
    internal static string Emit(CanonicalSyncEmissionModel model)
    {
        if (model.Compositions.Count == 0) return "";
        var b = Header();
        foreach (var c in model.Compositions)
        {
            var h = c.Handler;
            var result = h.ResultName ?? "void";
            b.AppendLine($$"""
                namespace {{c.Namespace}}
                {
                    public interface IZendiator
                    {
                        {{result}} Send({{h.RequestName}} request, global::System.Threading.CancellationToken cancellationToken = default);
                    }
                    public sealed class Zendiator : IZendiator
                    {
                        private readonly global::System.IServiceProvider _provider;
                        public Zendiator(global::System.IServiceProvider provider)
                        {
                            global::System.ArgumentNullException.ThrowIfNull(provider);
                            if (provider is not {{c.MicrosoftDiProviderName}} &&
                                (provider.GetType().Assembly != typeof({{c.MicrosoftDiProviderName}}).Assembly ||
                                 provider.GetType().FullName != "Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceProviderEngineScope"))
                                throw new global::System.NotSupportedException("This generated composition currently supports Microsoft Dependency Injection providers only.");
                            _provider = provider;
                        }
                        public {{result}} Send({{h.RequestName}} request, global::System.Threading.CancellationToken cancellationToken = default)
                        {
                            global::System.ArgumentNullException.ThrowIfNull(request);
                            cancellationToken.ThrowIfCancellationRequested();
                """);
            if (c.Hook != null)
            {
                EmitHookSend(b, c);
                b.AppendLine("        }\n    }");
                EmitRegistrar(b, c);
                b.AppendLine("}");
                continue;
            }
            var owned = c.Factory?.CleanupContractName != null;
            if (c.Factory is { } f)
                b.AppendLine($$"""
                            var factory = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{{f.FactoryName}}>(_provider);
                            {{h.HandlerName}}? handler = (({{f.ContractName}})factory).{{f.MethodName}}(request, cancellationToken)
                                ?? throw new global::System.InvalidOperationException("A session factory must return a nonnull Handler session.");
                """);
            else
                b.AppendLine($$"""            var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{{h.HandlerName}}>(_provider);""");
            if (owned) b.AppendLine("            try\n            {");
            b.AppendLine($$"""            {{(h.ResultName == null ? "" : "return ")}}(({{h.ContractName}})handler).{{h.MethodName}}(request, cancellationToken);""");
            if (owned)
                b.AppendLine($$"""
                            }
                            finally
                            {
                                var detached = handler;
                                handler = null;
                                (({{c.Factory!.CleanupContractName}})detached).Dispose();
                            }
                """);
            b.AppendLine("        }\n    }");
            EmitRegistrar(b, c);
            b.AppendLine("}");
        }
        return b.ToString();
    }

    private static void EmitHookSend(StringBuilder b, CanonicalSyncCompositionEmission c)
    {
        var h = c.Handler;
        var hook = c.Hook!;
        b.AppendLine($$"""
                    var hook = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{{hook.HookName}}>(_provider);
                    var saved = request;
                    var success = false;
                    global::System.Exception? error = null;
                    try
                    {
                        saved = (({{hook.Before.ContractName}})hook).{{hook.Before.MethodName}}(saved, cancellationToken);
                        global::System.ArgumentNullException.ThrowIfNull(saved);
                        cancellationToken.ThrowIfCancellationRequested();
                        var handler = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{{h.HandlerName}}>(_provider);
                        {{(h.ResultName == null ? "" : "var response = ")}}(({{h.ContractName}})handler).{{h.MethodName}}(saved, cancellationToken);
                        {{(h.ResultName == null ? "" : "response = ")}}(({{hook.Success.ContractName}})hook).{{hook.Success.MethodName}}(saved, {{(h.ResultName == null ? "" : "response, ")}}cancellationToken);
                        success = true;
                        {{(h.ResultName == null ? "" : "return response;")}}
                    }
                    catch (global::System.Exception failure)
                    {
                        error = failure;
                        try { (({{hook.Exception.ContractName}})hook).{{hook.Exception.MethodName}}(saved, failure, cancellationToken); }
                        catch (global::System.Exception replacement) { error = replacement; throw; }
                        throw;
                    }
                    finally
                    {
                        (({{hook.Finally.ContractName}})hook).{{hook.Finally.MethodName}}(saved, new global::Zendiator.SendExitInfo(success, error), cancellationToken);
                    }
            """);
    }

    private static void EmitRegistrar(StringBuilder b, CanonicalSyncCompositionEmission c)
    {
        var h = c.Handler;
        b.AppendLine("""
                internal static class ZendiatorGeneratedRegistrar
                {
                    internal static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add(
                        global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                        global::Zendiator.DependencyInjection.ZendiatorConfigurationSnapshot snapshot)
                    {
                        global::System.ArgumentNullException.ThrowIfNull(services);
                        global::System.ArgumentNullException.ThrowIfNull(snapshot);
                        if (services.GetType() != typeof(global::Microsoft.Extensions.DependencyInjection.ServiceCollection))
                            throw new global::System.NotSupportedException("This generated composition currently supports Microsoft Dependency Injection service collections only.");
                        var expected = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
            """);
        if (c.UseLocalHandlers) b.AppendLine("            expected.UseLocalHandlers();");
        var entry = c.Factory == null ? $$"""typeof({{h.HandlerName}})""" :
            $$"""global::Zendiator.HandlerEntry.Session<{{h.HandlerName}}, {{c.Factory.FactoryName}}>()""";
        b.AppendLine($$"""            expected.Handlers<{{h.RequestName}}>(global::Zendiator.DispatchFamily.SendSync, {{entry}});""");
        if (c.Hook is { } hook)
            b.AppendLine($$"""            expected.Pipeline<{{h.RequestName}}>({{(c.PipelineFamily == null ? "" : "global::Zendiator.DispatchFamily.SendSync, ")}}global::Zendiator.Stage.Hook<{{hook.HookName}}>());""");
        else if (c.HasEmptyPipeline)
            b.AppendLine($$"""            expected.Pipeline<{{h.RequestName}}>({{(c.PipelineFamily == null ? "" : "global::Zendiator.DispatchFamily.SendSync")}});""");
        b.AppendLine("""
                        if (snapshot.GetFingerprint() != expected.Snapshot().GetFingerprint())
                            throw new global::System.InvalidOperationException("AddZendiator configuration does not match this generated composition.");
            """);
        var service = c.Factory?.FactoryName ?? h.HandlerName;
        var types = c.Hook == null ? new[] { service, "global::" + c.Namespace + ".Zendiator" } :
            new[] { c.Hook.HookName, service, "global::" + c.Namespace + ".Zendiator" };
        foreach (var type in types)
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

    internal static string EmitInterceptors(CanonicalSyncInterceptorModel model)
    {
        if (model.CallSites.Count == 0) return "";
        var b = Header();
        b.AppendLine("""
            namespace System.Runtime.CompilerServices
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                file sealed class InterceptsLocationAttribute : global::System.Attribute
                {
                    public InterceptsLocationAttribute(int version, string data) { }
                }
            }
            namespace Zendiator.Generated.Interceptors
            {
                static class CanonicalSyncShims
                {
            """);
        for (var i = 0; i < model.CallSites.Count; i++)
        {
            var c = model.CallSites[i];
            b.AppendLine($$"""
                        [global::System.Runtime.CompilerServices.InterceptsLocation({{c.Version}}, "{{Escape(c.Data)}}")]
                        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Shim{{i}}{{(c.MarkerName == null ? "" : "<TComposition>")}}(
                            {{(c.ExtensionForm ? "this " : "")}}global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                            global::System.Action<global::Zendiator.DependencyInjection.ZendiatorConfiguration>{{(c.MarkerName == null ? "?" : "")}} configure)
                        {
                            global::System.ArgumentNullException.ThrowIfNull(services);
                            global::System.ArgumentNullException.ThrowIfNull(configure);
                """);
            if (c.MarkerName != null)
                b.AppendLine($$"""
                            if (typeof(TComposition) != typeof({{c.MarkerName}}))
                                throw new global::System.InvalidOperationException("The generated composition marker does not match this call.");
                    """);
            b.AppendLine($$"""
                            var configuration = new global::Zendiator.DependencyInjection.ZendiatorConfiguration();
                            configure(configuration);
                            return global::{{c.Namespace}}.ZendiatorGeneratedRegistrar.Add(services, configuration.Snapshot());
                        }
                """);
        }
        b.AppendLine("    }\n}");
        return b.ToString();
    }

    private static StringBuilder Header() => new StringBuilder().AppendLine("// <auto-generated/>\n#nullable enable");
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
