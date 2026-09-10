using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static void EmitOpenRoute(StringBuilder b, Route route, int index, INamedTypeSymbol handlerDefinition, INamedTypeSymbol behaviorDefinition, INamedTypeSymbol voidHandlerDefinition, INamedTypeSymbol voidBehaviorDefinition)
    {
        // Open routes resolve closed services per send; the shared DI container
        // keeps scoped/singleton reuse separated by closed type.
        var tp = string.Join(", ", route.OpenTypeParams);
        b.AppendLine("    /// <summary>Dispatches the request through its configured pipeline.</summary>");
        b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        b.Append("    public ").Append(Signature(route)).AppendLine("\n    {");
        Guard(b, route, "        ");
        b.Append("        return new OpenRoute").Append(index).Append("Node0<").Append(tp).Append(">(_services).InvokeAsync(request, cancellationToken);\n    }");
        for (var node = 0; node <= route.Behaviors.Count; node++)
        {
            b.Append("    private readonly struct OpenRoute").Append(index).Append("Node").Append(node).Append("<").Append(tp)
                .Append(">(global::System.IServiceProvider services) : ").Append(ContinuationContract(route));
            foreach (var c in route.MethodConstraints) b.Append(c);
            b.AppendLine("\n    {");
            b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("        public ").Append(TaskContract(route)).Append(" InvokeAsync(").Append(route.RequestDisplay)
                .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
            Guard(b, route, "            ");
            if (node == route.Behaviors.Count)
            {
                var directH = UseDirectCall(route.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition);
                var castH = "((" + route.HandlerContractDisplay + ")";
                b.Append("            return ").Append(directH
                    ? $"services.GetRequiredService<{route.HandlerDisplay}>()"
                    : castH + $"services.GetRequiredService<{route.HandlerDisplay}>())").AppendLine(".HandleAsync(request, cancellationToken);");
            }
            else
            {
                var directB = UseDirectCall(route.Behaviors[node], route.IsVoid ? voidBehaviorDefinition : behaviorDefinition);
                var castB = "((" + route.BehaviorContractDisplays[node] + ")";
                b.Append("            return ").Append(directB
                    ? $"services.GetRequiredService<{route.BehaviorDisplays[node]}>()"
                    : castB + $"services.GetRequiredService<{route.BehaviorDisplays[node]}>())")
                    .Append(".HandleAsync(request, new OpenRoute").Append(index).Append("Node").Append(node + 1)
                    .Append("<").Append(tp).Append(">(services), cancellationToken);");
            }
            b.AppendLine("        }\n    }");
        }
    }

    private static string Signature(Route route)
    {
        if (route.IsOpen)
        {
            var tp = string.Join(", ", route.OpenTypeParams);
            var task = route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $"global::System.Threading.Tasks.ValueTask<{route.ResponseDisplay}>";
            return $"{task} SendAsync<{tp}>({route.RequestDisplay} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
        }
        return route.IsVoid
        ? $"global::System.Threading.Tasks.ValueTask SendAsync({Name(route.Request)} request, global::System.Threading.CancellationToken cancellationToken = default)"
        : $"global::System.Threading.Tasks.ValueTask<{Name(route.Response)}> SendAsync({Name(route.Request)} request, global::System.Threading.CancellationToken cancellationToken = default)";
    }

    private static string HandlerContract(Route route) => route.IsVoid
        ? $"global::Zendiator.IRequestHandler<{Name(route.Request)}>"
        : $"global::Zendiator.IRequestHandler<{Name(route.Request)}, {Name(route.Response)}>";

    private static string BehaviorContract(Route route) => route.IsVoid
        ? $"global::Zendiator.IPipelineBehavior<{Name(route.Request)}>"
        : $"global::Zendiator.IPipelineBehavior<{Name(route.Request)}, {Name(route.Response)}>";

    private static string ContinuationContract(Route route)
    {
        if (route.IsOpen) return route.IsVoid
            ? $"global::Zendiator.IRequestContinuation<{route.RequestDisplay}>"
            : $"global::Zendiator.IRequestContinuation<{route.RequestDisplay}, {route.ResponseDisplay}>";
        return route.IsVoid
        ? $"global::Zendiator.IRequestContinuation<{Name(route.Request)}>"
        : $"global::Zendiator.IRequestContinuation<{Name(route.Request)}, {Name(route.Response)}>";
    }

    private static string TaskContract(Route route)
    {
        if (route.IsOpen) return route.IsVoid
            ? "global::System.Threading.Tasks.ValueTask"
            : $"global::System.Threading.Tasks.ValueTask<{route.ResponseDisplay}>";
        return route.IsVoid
        ? "global::System.Threading.Tasks.ValueTask"
        : $"global::System.Threading.Tasks.ValueTask<{Name(route.Response)}>";
    }

    private static void HandlerCall(StringBuilder b, Route route, string services, string indent)
    {
        b.Append(indent).Append("return ((").Append(HandlerContract(route)).Append(")").Append(services).Append(".GetRequiredService<").Append(Name(route.Handler))
            .AppendLine(">()).HandleAsync(request, cancellationToken);");
    }

    private static void HandlerCallDirect(StringBuilder b, Route route)
    {
        b.Append("            return services.GetRequiredService<").Append(Name(route.Handler))
            .AppendLine(">().HandleAsync(request, cancellationToken);");
    }

    private static void Guard(StringBuilder b, Route route, string indent)
    {
        if (route.Request.IsReferenceType) b.Append(indent).AppendLine("global::System.ArgumentNullException.ThrowIfNull(request);");
        b.Append(indent).AppendLine("cancellationToken.ThrowIfCancellationRequested();");
    }
}
