using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static string StreamSignature(Route route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
        return $"global::System.Collections.Generic.IAsyncEnumerable<{item}> StreamAsync{tp}({req} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
    }

    private static void EmitStreams(StringBuilder b, List<Route> streamRoutes, INamedTypeSymbol streamHandlerDefinition, INamedTypeSymbol streamBehaviorDefinition)
    {
        for (var index = 0; index < streamRoutes.Count; index++)
        {
            var route = streamRoutes[index];
            var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var cont = $"global::Zendiator.IStreamContinuation<{req}, {item}>";
            var enumerable = $"StreamRoute{index}Enumerable";
            var enumerator = $"StreamRoute{index}Enumerator";
            b.AppendLine("    /// <summary>Streams items for the request through its configured pipeline. Enumeration is lazy and scope-safe.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public ").Append(StreamSignature(route)).AppendLine("\n    {");
            // Lazy: no handler execution, no DI resolution here. Only capture scope provider + request + token.
            b.Append("        return new ").Append(enumerable).Append(tp).AppendLine("(_services, request, cancellationToken);\n    }");
            // Typed enumerable: lightweight, no shared mutable state on the mediator.
            b.Append("    private sealed class ").Append(enumerable);
            if (route.IsOpen) b.Append(tp);
            b.Append("(global::System.IServiceProvider services, ").Append(req).Append(" request, global::System.Threading.CancellationToken apiToken) : global::System.Collections.Generic.IAsyncEnumerable<").Append(item).Append(">");
            if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
            b.AppendLine("\n    {");
            b.Append("        public global::System.Collections.Generic.IAsyncEnumerator<").Append(item).Append(">").Append(" GetAsyncEnumerator(global::System.Threading.CancellationToken cancellationToken = default) => new ").Append(enumerator).Append(tp).AppendLine("(services, request, apiToken, cancellationToken);");
            b.AppendLine("    }");
            // Typed enumerator: resolves pipeline once on first MoveNext, merges tokens only when different.
            b.Append("    private sealed class ").Append(enumerator);
            if (route.IsOpen) b.Append(tp);
            b.Append(" : global::System.Collections.Generic.IAsyncEnumerator<").Append(item).Append(">");
            if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
            b.AppendLine("\n    {");
            b.AppendLine("        private readonly global::System.IServiceProvider _services;");
            b.Append("        private readonly ").Append(req).AppendLine(" _request;");
            b.AppendLine("        private readonly global::System.Threading.CancellationToken _apiToken;");
            b.AppendLine("        private readonly global::System.Threading.CancellationToken _enumToken;");
            b.AppendLine("        private global::System.Threading.CancellationTokenSource? _linked;");
            b.Append("        private global::System.Collections.Generic.IAsyncEnumerator<").Append(item).AppendLine(">? _inner;");
            b.AppendLine("        private bool _started;");
            b.AppendLine("        private bool _done;");
            b.Append("        public ").Append(enumerator).Append("(global::System.IServiceProvider services, ").Append(req).AppendLine(" request, global::System.Threading.CancellationToken apiToken, global::System.Threading.CancellationToken enumToken)");
            b.AppendLine("        {");
            b.AppendLine("            _services = services;");
            b.AppendLine("            _request = request;");
            b.AppendLine("            _apiToken = apiToken;");
            b.AppendLine("            _enumToken = enumToken;");
            b.AppendLine("        }");
            b.Append("        public ").Append(item).AppendLine(" Current => _inner != null ? _inner.Current : default!;");
            b.AppendLine("        public async global::System.Threading.Tasks.ValueTask<bool> MoveNextAsync()");
            b.AppendLine("        {");
            b.AppendLine("            if (_done) return false;");
            b.AppendLine("            if (!_started)");
            b.AppendLine("            {");
            b.AppendLine("                _started = true;");
            if (route.Request.IsReferenceType)
                b.AppendLine("                global::System.ArgumentNullException.ThrowIfNull(_request);");
            b.AppendLine("                var effective = MergeStreamTokens(_apiToken, _enumToken, out var linked);");
            b.AppendLine("                _linked = linked;");
            b.AppendLine("                effective.ThrowIfCancellationRequested();");
            b.Append("                global::System.Collections.Generic.IAsyncEnumerable<").Append(item).Append("> pipeline = new StreamRoute").Append(index).Append("Node0").Append(tp).Append("(_services).InvokeAsync(_request, effective);");
            b.AppendLine("");
            b.Append("                _inner = pipeline.GetAsyncEnumerator(effective);");
            b.AppendLine("");
            b.AppendLine("            }");
            b.AppendLine("            try");
            b.AppendLine("            {");
            b.AppendLine("                var ok = await _inner!.MoveNextAsync().ConfigureAwait(false);");
            b.AppendLine("                if (!ok) _done = true;");
            b.AppendLine("                return ok;");
            b.AppendLine("            }");
            b.AppendLine("            catch");
            b.AppendLine("            {");
            b.AppendLine("                _done = true;");
            b.AppendLine("                throw;");
            b.AppendLine("            }");
            b.AppendLine("        }");
            b.AppendLine("        public async global::System.Threading.Tasks.ValueTask DisposeAsync()");
            b.AppendLine("        {");
            b.AppendLine("            _done = true;");
            b.AppendLine("            var inner = _inner;");
            b.AppendLine("            _inner = null;");
            b.AppendLine("            try");
            b.AppendLine("            {");
            b.AppendLine("                if (inner != null) await inner.DisposeAsync().ConfigureAwait(false);");
            b.AppendLine("            }");
            b.AppendLine("            finally");
            b.AppendLine("            {");
            b.AppendLine("                _linked?.Dispose();");
            b.AppendLine("                _linked = null;");
            b.AppendLine("            }");
            b.AppendLine("        }");
            b.AppendLine("    }");
            for (var node = 0; node <= route.Behaviors.Count; node++)
            {
                b.Append("    private readonly struct StreamRoute").Append(index).Append("Node").Append(node);
                if (route.IsOpen) b.Append(tp);
                b.Append("(global::System.IServiceProvider services) : ").Append(cont);
                if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
                b.AppendLine("\n    {");
                b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                b.Append("        public global::System.Collections.Generic.IAsyncEnumerable<").Append(item).Append("> InvokeAsync(").Append(req)
                    .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                if (node == route.Behaviors.Count)
                {
                    string handlerName, contract;
                    if (route.IsOpen)
                    {
                        handlerName = route.HandlerDisplay;
                        contract = route.HandlerContractDisplay;
                    }
                    else
                    {
                        handlerName = Name(route.Handler);
                        contract = $"global::Zendiator.IStreamRequestHandler<{req}, {item}>";
                    }
                    var direct = UseDirectCall(route.Handler, streamHandlerDefinition);
                    var recv = direct
                        ? $"services.GetRequiredService<{handlerName}>()"
                        : "((" + contract + $")services.GetRequiredService<{handlerName}>())";
                    b.Append("            return ").Append(recv).AppendLine(".HandleAsync(request, cancellationToken);");
                }
                else
                {
                    string behaviorName, contract;
                    if (route.IsOpen)
                    {
                        behaviorName = route.BehaviorDisplays[node];
                        contract = route.BehaviorContractDisplays[node];
                    }
                    else
                    {
                        behaviorName = Name(route.Behaviors[node]);
                        contract = $"global::Zendiator.IStreamPipelineBehavior<{req}, {item}>";
                    }
                    var direct = UseDirectCall(route.Behaviors[node], streamBehaviorDefinition);
                    var recv = direct
                        ? $"services.GetRequiredService<{behaviorName}>()"
                        : "((" + contract + $")services.GetRequiredService<{behaviorName}>())";
                    b.Append("            return ").Append(recv)
                        .Append(".HandleAsync(request, new StreamRoute").Append(index).Append("Node").Append(node + 1);
                    if (route.IsOpen) b.Append(tp);
                    b.AppendLine("(services), cancellationToken);");
                }
                b.AppendLine("        }\n    }");
            }
        }
    }
}
