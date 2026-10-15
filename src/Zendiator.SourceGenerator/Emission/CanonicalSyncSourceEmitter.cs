using System.Linq;
using System.Text;

namespace Zendiator.SourceGenerator;

/// <summary>Emits the limited stage-free, single-Hook and two-Hook SendSync matrix.</summary>
internal static class CanonicalSyncSourceEmitter
{
    internal static string Emit(CanonicalSyncEmissionModel model)
    {
        if (model.Compositions.Count == 0) return "";
        var b = Header();
        b.Append(CanonicalSymbolName.Declarations(model.Compositions.SelectMany(c => c.ExternAliases)));
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
                            _provider = provider;
                        }
                        public {{result}} Send({{h.RequestName}} request, global::System.Threading.CancellationToken cancellationToken = default)
                        {
                            {{CanonicalCapabilitySourceEmitter.RequestGuard(h.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(request);")}}
                            cancellationToken.ThrowIfCancellationRequested();
                """);
            if (c.Hooks.Count != 0)
            {
                if (c.Hooks.Count == 1) EmitHookSend(b, c);
                else EmitNestedHookSend(b, c);
                b.AppendLine("        }\n    }");
                EmitRegistrar(b, c);
                b.AppendLine("}");
                continue;
            }
            var owned = c.Factory?.CleanupContractName != null;
            if (c.Factory is { } f)
                b.AppendLine($$"""
                            var factory = {{CanonicalServiceResolution.Emit(f.FactoryName, f.FactoryIsValueType)}};
                            {{h.HandlerName}}? handler = (({{f.ContractName}})factory).{{f.MethodName}}(request, cancellationToken)
                                ?? throw new global::System.InvalidOperationException("A session factory must return a nonnull Handler session.");
                """);
            else
                b.AppendLine($$"""            var handler = {{CanonicalServiceResolution.Emit(h.HandlerName, h.HandlerIsValueType)}};""");
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
        var hook = c.Hooks[0];
        var owned = hook.Factory?.CleanupContractName != null;
        if (hook.Factory is { } factory)
            b.AppendLine($$"""
                    var hookFactory = {{CanonicalServiceResolution.Emit(factory.FactoryName, factory.FactoryIsValueType)}};
                    {{hook.HookName}}? hook = (({{factory.ContractName}})hookFactory).{{factory.MethodName}}(request, cancellationToken)
                        ?? throw new global::System.InvalidOperationException("A session factory must return a nonnull Hook session.");
                """);
        else
            b.AppendLine($$"""            var hook = {{CanonicalServiceResolution.Emit(hook.HookName, hook.HookIsValueType)}};""");
        if (owned) b.AppendLine("            try\n            {");
        var handlerCall = HookHandlerCall(c);
        b.AppendLine($$"""
                    var saved = request;
                    var success = false;
                    global::System.Exception? error = null;
                    try
                    {
                        saved = (({{hook.Before.ContractName}})hook).{{hook.Before.MethodName}}(saved, cancellationToken);
                        {{CanonicalCapabilitySourceEmitter.RequestGuard(h.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(saved" + (c.Factory == null ? "" : ", \"request\"") + ");")}}
                        cancellationToken.ThrowIfCancellationRequested();
                        {{handlerCall}}
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
        if (owned)
            b.AppendLine($$"""
                        }
                        finally
                        {
                            var detached = hook;
                            hook = null;
                            (({{hook.Factory!.CleanupContractName}})detached).Dispose();
                        }
                """);
    }

    private static string HookHandlerCall(CanonicalSyncCompositionEmission c)
    {
        var h = c.Handler;
        if (c.Factory is not { } factory)
            return $$"""
                var handler = {{CanonicalServiceResolution.Emit(h.HandlerName, h.HandlerIsValueType)}};
                        {{(h.ResultName == null ? "" : "var response = ")}}(({{h.ContractName}})handler).{{h.MethodName}}(saved, cancellationToken);
                """;
        var owned = factory.CleanupContractName != null;
        return $$"""
                var handlerFactory = {{CanonicalServiceResolution.Emit(factory.FactoryName, factory.FactoryIsValueType)}};
                        {{h.HandlerName}}? handler = (({{factory.ContractName}})handlerFactory).{{factory.MethodName}}(saved, cancellationToken)
                            ?? throw new global::System.InvalidOperationException("A session factory must return a nonnull Handler session.");
                        {{(h.ResultName == null ? "" : h.ResultName + " response;")}}
                        {{(owned ? "try\n                    {" : "")}}
                        {{(h.ResultName == null ? "" : "response = ")}}(({{h.ContractName}})handler).{{h.MethodName}}(saved, cancellationToken);
                        {{(owned ? "}\n                    finally\n                    {\n                        var detached = handler;\n                        handler = null;\n                        ((" + factory.CleanupContractName + ")detached).Dispose();\n                    }" : "")}}
                """;
    }

    private static void EmitNestedHookSend(StringBuilder b, CanonicalSyncCompositionEmission c)
    {
        var h = c.Handler;
        var outer = c.Hooks[0]; var inner = c.Hooks[1];
        b.AppendLine($$"""
                    var outer = {{CanonicalServiceResolution.Emit(outer.HookName, outer.HookIsValueType)}};
                    var savedOuter = request;
                    var successOuter = false;
                    global::System.Exception? errorOuter = null;
                    try
                    {
                        savedOuter = (({{outer.Before.ContractName}})outer).{{outer.Before.MethodName}}(savedOuter, cancellationToken);
                        {{CanonicalCapabilitySourceEmitter.RequestGuard(h.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(savedOuter, \"request\");")}}
                        cancellationToken.ThrowIfCancellationRequested();
                        var inner = {{CanonicalServiceResolution.Emit(inner.HookName, inner.HookIsValueType)}};
                        var savedInner = savedOuter;
                        var successInner = false;
                        global::System.Exception? errorInner = null;
                        {{(h.ResultName == null ? "" : h.ResultName + " response;")}}
                        try
                        {
                            savedInner = (({{inner.Before.ContractName}})inner).{{inner.Before.MethodName}}(savedInner, cancellationToken);
                            {{CanonicalCapabilitySourceEmitter.RequestGuard(h.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(savedInner, \"saved\");")}}
                            cancellationToken.ThrowIfCancellationRequested();
                            var handler = {{CanonicalServiceResolution.Emit(h.HandlerName, h.HandlerIsValueType)}};
                            {{(h.ResultName == null ? "" : "response = ")}}(({{h.ContractName}})handler).{{h.MethodName}}(savedInner, cancellationToken);
                            {{(h.ResultName == null ? "" : "response = ")}}(({{inner.Success.ContractName}})inner).{{inner.Success.MethodName}}(savedInner, {{(h.ResultName == null ? "" : "response, ")}}cancellationToken);
                            successInner = true;
                        }
                        catch (global::System.Exception innerFailure)
                        {
                            errorInner = innerFailure;
                            try { (({{inner.Exception.ContractName}})inner).{{inner.Exception.MethodName}}(savedInner, innerFailure, cancellationToken); }
                            catch (global::System.Exception innerReplacement) { errorInner = innerReplacement; throw; }
                            throw;
                        }
                        finally
                        {
                            (({{inner.Finally.ContractName}})inner).{{inner.Finally.MethodName}}(savedInner, new global::Zendiator.SendExitInfo(successInner, errorInner), cancellationToken);
                        }
                        {{(h.ResultName == null ? "" : "response = ")}}(({{outer.Success.ContractName}})outer).{{outer.Success.MethodName}}(savedOuter, {{(h.ResultName == null ? "" : "response, ")}}cancellationToken);
                        successOuter = true;
                        {{(h.ResultName == null ? "" : "return response;")}}
                    }
                    catch (global::System.Exception outerFailure)
                    {
                        errorOuter = outerFailure;
                        try { (({{outer.Exception.ContractName}})outer).{{outer.Exception.MethodName}}(savedOuter, outerFailure, cancellationToken); }
                        catch (global::System.Exception outerReplacement) { errorOuter = outerReplacement; throw; }
                        throw;
                    }
                    finally
                    {
                        (({{outer.Finally.ContractName}})outer).{{outer.Finally.MethodName}}(savedOuter, new global::Zendiator.SendExitInfo(successOuter, errorOuter), cancellationToken);
                    }
            """);
    }

    internal static void EmitRegistrar(StringBuilder b, CanonicalSyncCompositionEmission c, BoundDispatchFamily family = BoundDispatchFamily.SendSync,
        BoundStreamOpenRegistration? open = null, string? streamFinalItemName = null,
        EquatableArray<RegistrationStageEmission> streamDeclaredStages = default) =>
        CanonicalRegistrationSourceEmitter.Emit(b, CanonicalRegistrationPlan.Single(c, family, open, streamFinalItemName, streamDeclaredStages));

    internal static string EmitInterceptors(CanonicalSyncInterceptorModel model, string shimName = "CanonicalSyncShims")
    {
        if (model.CallSites.Count == 0) return "";
        var b = Header();
        b.Append(CanonicalSymbolName.Declarations(model.CallSites.SelectMany(c => c.ExternAliases)));
        b.AppendLine($$"""
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
                static class {{shimName}}
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
