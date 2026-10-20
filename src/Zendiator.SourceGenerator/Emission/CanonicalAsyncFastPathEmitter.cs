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
        var builder = "global::System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder" + (h.ResultName == null ? "" : "<" + h.ResultName + ">");
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
                        var invocation = new SendInvocation{{generic}}(_provider, request, cancellationToken);
                        // Start preserves the native async prefix's ExecutionContext and SynchronizationContext boundary.
                        var boundary = global::System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder.Create();
                        boundary.Start(ref invocation);
                        return invocation.Operation;
                    }
                    private struct SendInvocation{{generic}} : global::System.Runtime.CompilerServices.IAsyncStateMachine{{constraints}}
                    {
                        private readonly global::System.IServiceProvider _provider;
                        private readonly {{h.RequestName}} _request;
                        private readonly global::System.Threading.CancellationToken _cancellationToken;
                        internal {{result}} Operation;
                        internal SendInvocation(global::System.IServiceProvider provider, {{h.RequestName}} request,
                            global::System.Threading.CancellationToken cancellationToken)
                        {
                            _provider = provider; _request = request; _cancellationToken = cancellationToken; Operation = default;
                        }
                        public void SetStateMachine(global::System.Runtime.CompilerServices.IAsyncStateMachine stateMachine) { }
                        public void MoveNext()
                        {
                            try
                            {
                                var request = _request;
                                var cancellationToken = _cancellationToken;
                                cancellationToken.ThrowIfCancellationRequested();
            """);
        for (var i = 0; i < hooks.Length; i++)
        {
            var hook = hooks[i];
            b.AppendLine($$"""
                                var hook{{i}} = {{CanonicalServiceResolution.Emit(hook.HookName, hook.HookIsValueType)}};
                                request = Before{{i}}(hook{{i}}, request, cancellationToken);
                                {{CanonicalCapabilitySourceEmitter.RequestGuard(h.RequestIsValueType, "global::System.ArgumentNullException.ThrowIfNull(request);")}}
                                cancellationToken.ThrowIfCancellationRequested();
                """);
        }
        b.AppendLine($$"""
                                var handler = {{CanonicalServiceResolution.Emit(h.HandlerName, h.HandlerIsValueType)}};
                                var operation = Handle(handler, request, cancellationToken);
                                if (operation.IsCompletedSuccessfully)
                                {
                                    {{(h.ResultName == null ? "operation.GetAwaiter().GetResult(); Operation = default;" : "Operation = new(operation.GetAwaiter().GetResult());")}}
                                }
                                else Operation = Finish(operation);
                            }
                            catch (global::System.Exception error)
                            {
                                var failure = {{builder}}.Create();
                                failure.SetException(error);
                                Operation = failure.Task;
                            }
                        }
                        private static async {{result}} Finish({{result}} operation)
                        {
                            {{(h.ResultName == null ? "" : "return ")}}await operation.ConfigureAwait(false);
                        }
            """);
        const string token = "global::System.Threading.CancellationToken";
        for (var i = 0; i < hooks.Length; i++)
        {
            var hook = hooks[i]; var callback = hook.Before!;
            CanonicalAsyncCallEmitter.EmitCall(b, "Before" + i, h.RequestName, hook.HookIsValueType ? "object" : hook.HookName,
                callback.ContractName, callback.MethodName, callback.CallTarget,
                new[] { h.RequestName, token }, new[] { "request", "cancellationToken" });
        }
        CanonicalAsyncCallEmitter.EmitCall(b, "Handle", result, h.HandlerIsValueType ? "object" : h.HandlerName,
            h.ContractName, h.MethodName, handlerTarget, new[] { h.RequestName, token }, new[] { "request", "cancellationToken" });
        b.AppendLine("        }\n    }");
        CanonicalSyncSourceEmitter.EmitRegistrar(b, registration, BoundDispatchFamily.SendAsync);
        b.AppendLine("}");
    }
}
