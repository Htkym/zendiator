using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static void EmitSyncMulti(
        StringBuilder b,
        List<MultiRoute> syncMultiRoutes,
        INamedTypeSymbol syncHandlerDefinition,
        INamedTypeSymbol syncVoidHandlerDefinition,
        INamedTypeSymbol syncBehaviorDefinition,
        INamedTypeSymbol syncVoidBehaviorDefinition)
    {
        for (var index = 0; index < syncMultiRoutes.Count; index++)
        {
            var route = syncMultiRoutes[index];
            var tp = TypeParameters(route);
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var resp = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var cont = route.IsVoid ? $$"""global::Zendiator.ISyncRequestContinuation<{{req}}>""" : $$"""global::Zendiator.ISyncRequestContinuation<{{req}}, {{resp}}>""";
            var scoped = NeedsScoped(route) ? "scoped " : "";
            b.AppendLine($$"""
                    /// <summary>Dispatches the request to every handler in order, synchronously.</summary>
                    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                    public {{SyncMultiSignature(route)}}
                    {
                """);
            if (route.Request.IsReferenceType)
                b.AppendLine("""        global::System.ArgumentNullException.ThrowIfNull(request);""");
            b.AppendLine("""        cancellationToken.ThrowIfCancellationRequested();""");
            if (!route.IsVoid)
                b.AppendLine($$"""
                            var results = new global::System.Collections.Generic.List<{{resp}}>({{route.Branches.Count}});
                    """);
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var node0 = $$"""SyncMultiRoute{{index}}Branch{{bh}}Node0{{tp}}""";
                if (route.IsVoid)
                    b.AppendLine($$"""        new {{node0}}(_services).Invoke(request, cancellationToken);""");
                else
                    b.AppendLine($$"""        results.Add(new {{node0}}(_services).Invoke(request, cancellationToken));""");
            }

            if (!route.IsVoid)
                b.AppendLine("""        return results;""");
            b.AppendLine("""    }""");
            for (var bh = 0; bh < route.Branches.Count; bh++)
            {
                var branch = route.Branches[bh];
                var prefix = $$"""SyncMultiRoute{{index}}Branch{{bh}}""";
                var branchContract = route.IsVoid ? $$"""global::Zendiator.ISyncRequestHandler<{{req}}>""" : $$"""global::Zendiator.ISyncRequestHandler<{{req}}, {{resp}}>""";
                var behaviorRouteContract = route.IsVoid ? $$"""global::Zendiator.ISyncPipelineBehavior<{{req}}>""" : $$"""global::Zendiator.ISyncPipelineBehavior<{{req}}, {{resp}}>""";
                for (var node = 0; node <= branch.Behaviors.Count; node++)
                {
                    b.AppendLine($$"""
                            private readonly struct {{prefix}}Node{{node}}{{TypeParameters(route)}}(global::Zendiator.DependencyInjection.ZendiatorServiceResolver services) : {{cont}}{{TypeConstraints(route)}}
                            {
                                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                                public {{(route.IsVoid ? "void" : resp)}} Invoke({{scoped}}{{req}} request, global::System.Threading.CancellationToken cancellationToken)
                                {
                        """);
                    if (route.Request.IsReferenceType)
                        b.AppendLine("""            global::System.ArgumentNullException.ThrowIfNull(request);""");
                    b.AppendLine("""            cancellationToken.ThrowIfCancellationRequested();""");
                    if (node == branch.Behaviors.Count)
                    {
                        var handlerName = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerDisplay : Name(branch.Handler);
                        var contract = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerContractDisplay : branchContract;
                        var direct = UseDirectCall(branch.Handler, route.IsVoid ? syncVoidHandlerDefinition : syncHandlerDefinition, "Handle");
                        var recv = ServiceReceiver(handlerName, contract, direct);
                        b.Append("""            """);
                        if (!route.IsVoid)
                            b.Append("""return """);
                        b.AppendLine($$"""{{recv}}.Handle(request, cancellationToken);""");
                    }
                    else
                    {
                        var behaviorName = route.IsOpen ? branch.BehaviorDisplays[node] : Name(branch.Behaviors[node]);
                        var contract = route.IsOpen ? branch.BehaviorContractDisplays[node] : behaviorRouteContract;
                        var direct = UseDirectCall(branch.Behaviors[node], route.IsVoid ? syncVoidBehaviorDefinition : syncBehaviorDefinition, "Handle");
                        var recv = ServiceReceiver(behaviorName, contract, direct);
                        b.Append("""            """);
                        if (!route.IsVoid)
                            b.Append("""return """);
                        b.AppendLine($$"""
                            {{recv}}.Handle(request, new {{prefix}}Node{{node + 1}}{{TypeParameters(route)}}(services), cancellationToken);
                            """);
                    }

                    b.AppendLine("""
                                }
                            }
                        """);
                }
            }
        }
    }

    private static string SyncMultiSignature(MultiRoute route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var task = route.IsVoid ? "void" : $$"""
            global::System.Collections.Generic.IReadOnlyList<{{(route.IsOpen ? route.ResponseDisplay : Name(route.Response))}}>
            """;
        var tp = TypeParameters(route);
        var scoped = NeedsScoped(route) ? "scoped " : "";
        return $$"""
            {{task}} SendAllSync{{tp}}({{scoped}}{{req}} request, global::System.Threading.CancellationToken cancellationToken = default){{string.Concat(route.MethodConstraints)}}
            """;
    }
}
