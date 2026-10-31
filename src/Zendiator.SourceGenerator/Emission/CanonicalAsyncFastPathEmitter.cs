using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zendiator.SourceGenerator;

internal static class CanonicalAsyncFastPathEmitter
{
    internal static bool Supports(CanonicalCapabilityCompositionEmission c) =>
        c.Family == BoundDispatchFamily.SendAsync && c.HandlerFactory == null &&
        c.Hooks.All(h => h.Factory == null && h.Before is { Completion: BoundCompletionKind.Synchronous } &&
            h.Success == null && h.Exception == null && h.Finally == null);

    internal static void Emit(StringBuilder b, CanonicalSyncCompositionEmission registration,
        BoundAsyncCallTargetEmission? handlerTarget, IEnumerable<HookCapabilityEmission> beforeHooks)
    {
        var h = registration.Handler;
        var hooks = beforeHooks.ToArray();
        var generic = registration.Generic?.TypeParameters ?? "";
        var constraints = registration.Generic?.TypeConstraints ?? "";
        var result = "global::System.Threading.Tasks.ValueTask" + (h.ResultName == null ? "" : "<" + h.ResultName + ">");
        b.AppendLine($$"""
            namespace {{registration.Namespace}}
            {
                public interface IZendiator
                {
                    {{result}} SendAsync{{generic}}({{h.RequestName}} request, global::System.Threading.CancellationToken cancellationToken = default){{constraints}};
                }
                public sealed class Zendiator : IZendiator
                {
                    private readonly global::System.IServiceProvider _provider;
                    public Zendiator(global::System.IServiceProvider provider)
                    {
                        global::System.ArgumentNullException.ThrowIfNull(provider);
                        _provider = provider;
                    }
                    public {{result}} SendAsync{{generic}}({{h.RequestName}} request, global::System.Threading.CancellationToken cancellationToken = default){{constraints}}
                    {
                        {{CanonicalCapabilitySourceEmitter.RequestGuard(h.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(request);")}}
                        cancellationToken.ThrowIfCancellationRequested();
            """);
        for (var i = 0; i < hooks.Length; i++)
        {
            var hook = hooks[i];
            b.AppendLine($$"""
                        var hook{{i}} = {{CanonicalServiceResolution.Emit(hook.HookName, hook.HookIsValueType)}};
                        request = Before{{i}}{{generic}}(hook{{i}}, request, cancellationToken);
                        {{CanonicalCapabilitySourceEmitter.RequestGuard(h.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(request);")}}
                        cancellationToken.ThrowIfCancellationRequested();
                """);
        }
        b.AppendLine($$"""
                        var handler = {{CanonicalServiceResolution.Emit(h.HandlerName, h.HandlerIsValueType)}};
                        return Handle{{generic}}(handler, request, cancellationToken);
                    }
            """);
        const string token = "global::System.Threading.CancellationToken";
        for (var i = 0; i < hooks.Length; i++)
        {
            var hook = hooks[i];
            var callback = hook.Before!;
            CanonicalAsyncCallEmitter.EmitCall(b, "Before" + i, h.RequestName, hook.HookIsValueType ? "object" : hook.HookName,
                callback.ContractName, callback.MethodName, callback.CallTarget,
                new[] { h.RequestName, token }, new[] { "request", "cancellationToken" }, generic, constraints);
        }
        CanonicalAsyncCallEmitter.EmitCall(b, "Handle", result, h.HandlerIsValueType ? "object" : h.HandlerName,
            h.ContractName, h.MethodName, handlerTarget, new[] { h.RequestName, token }, new[] { "request", "cancellationToken" }, generic, constraints);
        b.AppendLine("    }");
        CanonicalSyncSourceEmitter.EmitRegistrar(b, registration, BoundDispatchFamily.SendAsync);
        b.AppendLine("}");
    }
}
