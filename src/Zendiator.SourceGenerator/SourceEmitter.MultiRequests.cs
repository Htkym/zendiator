using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static string MultiSignature(MultiRoute route)
    {
        if (route.IsOpen)
        {
            var tp = string.Join(", ", route.OpenTypeParams);
            var task = route.IsVoid
                ? "global::System.Threading.Tasks.ValueTask"
                : $"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IReadOnlyList<{route.ResponseDisplay}>>";
            return $"{task} SendAllAsync<{tp}>({route.RequestDisplay} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
        }
        var closedTask = route.IsVoid
            ? "global::System.Threading.Tasks.ValueTask"
            : $"global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IReadOnlyList<{Name(route.Response)}>>";
        return $"{closedTask} SendAllAsync({Name(route.Request)} request, global::System.Threading.CancellationToken cancellationToken = default)";
    }

    private static void EmitMulti(StringBuilder b, List<MultiRoute> multiRoutes, INamedTypeSymbol handlerDefinition, INamedTypeSymbol behaviorDefinition, INamedTypeSymbol voidHandlerDefinition, INamedTypeSymbol voidBehaviorDefinition)
    {
        for (var index = 0; index < multiRoutes.Count; index++)
        {
            var route = multiRoutes[index];
            var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
            var reqDisplay = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var respDisplay = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var contDisplay = route.IsVoid
                ? $"global::Zendiator.IRequestContinuation<{reqDisplay}>"
                : $"global::Zendiator.IRequestContinuation<{reqDisplay}, {respDisplay}>";
            var taskDisplay = route.IsVoid
                ? "global::System.Threading.Tasks.ValueTask"
                : $"global::System.Threading.Tasks.ValueTask<{respDisplay}>";
            var branchContract = route.IsVoid
                ? $"global::Zendiator.IRequestHandler<{reqDisplay}>"
                : $"global::Zendiator.IRequestHandler<{reqDisplay}, {respDisplay}>";
            var behaviorRouteContract = route.IsVoid
                ? $"global::Zendiator.IPipelineBehavior<{reqDisplay}>"
                : $"global::Zendiator.IPipelineBehavior<{reqDisplay}, {respDisplay}>";
            b.AppendLine("    /// <summary>Dispatches the request to every handler in order.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public async ").Append(MultiSignature(route)).AppendLine("\n    {");
            if (route.Request.IsReferenceType) b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(request);");
            b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            if (!route.IsVoid)
                b.Append("        var results = new global::System.Collections.Generic.List<").Append(respDisplay).Append(">(").Append(route.Branches.Count).AppendLine(");");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var node0 = $"MultiRoute{index}Branch{bh}Node0{tp}";
                if (route.IsVoid)
                    b.Append("        await new ").Append(node0).AppendLine("(_services).InvokeAsync(request, cancellationToken).ConfigureAwait(false);");
                else
                    b.Append("        results.Add(await new ").Append(node0).AppendLine("(_services).InvokeAsync(request, cancellationToken).ConfigureAwait(false));");
            }
            if (!route.IsVoid) b.AppendLine("        return results;");
            b.AppendLine("    }");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var branch = route.Branches[bh];
                var prefix = $"MultiRoute{index}Branch{bh}";
                for (var node = 0; node <= branch.Behaviors.Count; node++)
                {
                    b.Append("    private readonly struct ").Append(prefix).Append("Node").Append(node);
                    if (route.IsOpen) b.Append(tp);
                    b.Append("(global::System.IServiceProvider services) : ").Append(contDisplay);
                    if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
                    b.AppendLine("\n    {");
                    b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                    b.Append("        public ").Append(taskDisplay).Append(" InvokeAsync(").Append(reqDisplay)
                        .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                    if (route.Request.IsReferenceType) b.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(request);");
                    b.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
                    if (node == branch.Behaviors.Count)
                    {
                        var handlerName = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerDisplay : Name(branch.Handler);
                        var contract = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerContractDisplay : branchContract;
                        var direct = UseDirectCall(branch.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition);
                        var recv = direct
                            ? $"services.GetRequiredService<{handlerName}>()"
                            : "((" + contract + $")services.GetRequiredService<{handlerName}>())";
                        b.Append("            return ").Append(recv).AppendLine(".HandleAsync(request, cancellationToken);");
                    }
                    else
                    {
                        var behaviorName = route.IsOpen ? branch.BehaviorDisplays[node] : Name(branch.Behaviors[node]);
                        var contract = route.IsOpen ? branch.BehaviorContractDisplays[node] : behaviorRouteContract;
                        var direct = UseDirectCall(branch.Behaviors[node], route.IsVoid ? voidBehaviorDefinition : behaviorDefinition);
                        var recv = direct
                            ? $"services.GetRequiredService<{behaviorName}>()"
                            : "((" + contract + $")services.GetRequiredService<{behaviorName}>())";
                        b.Append("            return ").Append(recv)
                            .Append(".HandleAsync(request, new ").Append(prefix).Append("Node").Append(node + 1);
                        if (route.IsOpen) b.Append(tp);
                        b.AppendLine("(services), cancellationToken);");
                    }
                    b.AppendLine("        }\n    }");
                }
            }
        }
    }
}
