using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static bool NeedsScoped(Route route) => route.Request.IsRefLikeType;

    private static bool NeedsScoped(MultiRoute route) => route.Request.IsRefLikeType;

    private static string SyncSignature(Route route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var task = route.IsVoid ? "void" : (route.IsOpen ? route.ResponseDisplay : Name(route.Response));
        var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
        var scoped = NeedsScoped(route) ? "scoped " : "";
        return $"{task} SendSync{tp}({scoped}{req} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
    }

    private static string SyncMultiSignature(MultiRoute route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var task = route.IsVoid
            ? "void"
            : $"global::System.Collections.Generic.IReadOnlyList<{(route.IsOpen ? route.ResponseDisplay : Name(route.Response))}>";
        var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
        var scoped = NeedsScoped(route) ? "scoped " : "";
        return $"{task} SendAllSync{tp}({scoped}{req} request, global::System.Threading.CancellationToken cancellationToken = default){string.Concat(route.MethodConstraints)}";
    }

    private static void EmitSyncRoutes(StringBuilder b, List<Route> syncRoutes, INamedTypeSymbol syncHandlerDefinition, INamedTypeSymbol syncVoidHandlerDefinition, INamedTypeSymbol syncBehaviorDefinition, INamedTypeSymbol syncVoidBehaviorDefinition)
    {
        for (var index = 0; index < syncRoutes.Count; index++)
        {
            var route = syncRoutes[index];
            var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var resp = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var task = route.IsVoid ? "void" : resp;
            var cont = route.IsVoid
                ? $"global::Zendiator.ISyncRequestContinuation<{req}>"
                : $"global::Zendiator.ISyncRequestContinuation<{req}, {resp}>";
            var scoped = NeedsScoped(route) ? "scoped " : "";
            b.AppendLine("    /// <summary>Dispatches the request synchronously without retaining it.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public ").Append(SyncSignature(route)).AppendLine("\n    {");
            Guard(b, route, "        ");
            b.Append("        ");
            if (!route.IsVoid) b.Append("return ");
            b.Append("new SyncRoute").Append(index).Append("Node0").Append(tp).Append("(_services).Invoke(request, cancellationToken);\n    }");
            for (var node = 0; node <= route.Behaviors.Count; node++)
            {
                b.Append("    private readonly struct SyncRoute").Append(index).Append("Node").Append(node);
                if (route.IsOpen) b.Append(tp);
                b.Append("(global::System.IServiceProvider services) : ").Append(cont);
                if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
                b.AppendLine("\n    {");
                b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                b.Append("        public ").Append(task).Append(" Invoke(").Append(scoped).Append(req)
                    .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                Guard(b, route, "            ");
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
                        contract = route.IsVoid
                            ? $"global::Zendiator.ISyncRequestHandler<{req}>"
                            : $"global::Zendiator.ISyncRequestHandler<{req}, {resp}>";
                    }
                    var direct = UseDirectCall(route.Handler, route.IsVoid ? syncVoidHandlerDefinition : syncHandlerDefinition, "Handle");
                    var recv = direct
                        ? $"services.GetRequiredService<{handlerName}>()"
                        : "((" + contract + $")services.GetRequiredService<{handlerName}>())";
                    b.Append("            ");
                    if (!route.IsVoid) b.Append("return ");
                    b.Append(recv).AppendLine(".Handle(request, cancellationToken);");
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
                        contract = route.IsVoid
                            ? $"global::Zendiator.ISyncPipelineBehavior<{req}>"
                            : $"global::Zendiator.ISyncPipelineBehavior<{req}, {resp}>";
                    }
                    var direct = UseDirectCall(route.Behaviors[node], route.IsVoid ? syncVoidBehaviorDefinition : syncBehaviorDefinition, "Handle");
                    var recv = direct
                        ? $"services.GetRequiredService<{behaviorName}>()"
                        : "((" + contract + $")services.GetRequiredService<{behaviorName}>())";
                    b.Append("            ");
                    if (!route.IsVoid) b.Append("return ");
                    b.Append(recv).Append(".Handle(request, new SyncRoute").Append(index).Append("Node").Append(node + 1);
                    if (route.IsOpen) b.Append(tp);
                    b.AppendLine("(services), cancellationToken);");
                }
                b.AppendLine("        }\n    }");
            }
        }
    }

    private static void EmitSyncMulti(StringBuilder b, List<MultiRoute> syncMultiRoutes, INamedTypeSymbol syncHandlerDefinition, INamedTypeSymbol syncVoidHandlerDefinition, INamedTypeSymbol syncBehaviorDefinition, INamedTypeSymbol syncVoidBehaviorDefinition)
    {
        for (var index = 0; index < syncMultiRoutes.Count; index++)
        {
            var route = syncMultiRoutes[index];
            var tp = route.IsOpen ? "<" + string.Join(", ", route.OpenTypeParams) + ">" : "";
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var resp = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var cont = route.IsVoid
                ? $"global::Zendiator.ISyncRequestContinuation<{req}>"
                : $"global::Zendiator.ISyncRequestContinuation<{req}, {resp}>";
            var scoped = NeedsScoped(route) ? "scoped " : "";
            b.AppendLine("    /// <summary>Dispatches the request to every handler in order, synchronously.</summary>");
            b.AppendLine("    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            b.Append("    public ").Append(SyncMultiSignature(route)).AppendLine("\n    {");
            if (route.Request.IsReferenceType) b.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(request);");
            b.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
            if (!route.IsVoid)
                b.Append("        var results = new global::System.Collections.Generic.List<").Append(resp).Append(">(").Append(route.Branches.Count).AppendLine(");");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var node0 = $"SyncMultiRoute{index}Branch{bh}Node0{tp}";
                if (route.IsVoid)
                    b.Append("        new ").Append(node0).AppendLine("(_services).Invoke(request, cancellationToken);");
                else
                    b.Append("        results.Add(new ").Append(node0).AppendLine("(_services).Invoke(request, cancellationToken));");
            }
            if (!route.IsVoid) b.AppendLine("        return results;");
            b.AppendLine("    }");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var branch = route.Branches[bh];
                var prefix = $"SyncMultiRoute{index}Branch{bh}";
                var branchContract = route.IsVoid
                    ? $"global::Zendiator.ISyncRequestHandler<{req}>"
                    : $"global::Zendiator.ISyncRequestHandler<{req}, {resp}>";
                var behaviorRouteContract = route.IsVoid
                    ? $"global::Zendiator.ISyncPipelineBehavior<{req}>"
                    : $"global::Zendiator.ISyncPipelineBehavior<{req}, {resp}>";
                for (var node = 0; node <= branch.Behaviors.Count; node++)
                {
                    b.Append("    private readonly struct ").Append(prefix).Append("Node").Append(node);
                    if (route.IsOpen) b.Append(tp);
                    b.Append("(global::System.IServiceProvider services) : ").Append(cont);
                    if (route.IsOpen) foreach (var c in route.MethodConstraints) b.Append(c);
                    b.AppendLine("\n    {");
                    b.AppendLine("        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
                    b.Append("        public ").Append(route.IsVoid ? "void" : resp).Append(" Invoke(").Append(scoped).Append(req)
                        .AppendLine(" request, global::System.Threading.CancellationToken cancellationToken)\n        {");
                    if (route.Request.IsReferenceType) b.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(request);");
                    b.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
                    if (node == branch.Behaviors.Count)
                    {
                        var handlerName = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerDisplay : Name(branch.Handler);
                        var contract = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerContractDisplay : branchContract;
                        var direct = UseDirectCall(branch.Handler, route.IsVoid ? syncVoidHandlerDefinition : syncHandlerDefinition, "Handle");
                        var recv = direct
                            ? $"services.GetRequiredService<{handlerName}>()"
                            : "((" + contract + $")services.GetRequiredService<{handlerName}>())";
                        b.Append("            ");
                        if (!route.IsVoid) b.Append("return ");
                        b.Append(recv).AppendLine(".Handle(request, cancellationToken);");
                    }
                    else
                    {
                        var behaviorName = route.IsOpen ? branch.BehaviorDisplays[node] : Name(branch.Behaviors[node]);
                        var contract = route.IsOpen ? branch.BehaviorContractDisplays[node] : behaviorRouteContract;
                        var direct = UseDirectCall(branch.Behaviors[node], route.IsVoid ? syncVoidBehaviorDefinition : syncBehaviorDefinition, "Handle");
                        var recv = direct
                            ? $"services.GetRequiredService<{behaviorName}>()"
                            : "((" + contract + $")services.GetRequiredService<{behaviorName}>())";
                        b.Append("            ");
                        if (!route.IsVoid) b.Append("return ");
                        b.Append(recv).Append(".Handle(request, new ").Append(prefix).Append("Node").Append(node + 1);
                        if (route.IsOpen) b.Append(tp);
                        b.AppendLine("(services), cancellationToken);");
                    }
                    b.AppendLine("        }\n    }");
                }
            }
        }
    }
}
