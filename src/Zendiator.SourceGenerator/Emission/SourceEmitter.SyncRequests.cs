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
        var tp = TypeParameters(route);
        var scoped = NeedsScoped(route) ? "scoped " : "";
        return $$"""
            {{task}} SendSync{{tp}}({{scoped}}{{req}} request, global::System.Threading.CancellationToken cancellationToken = default){{string.Concat(route.MethodConstraints)}}
            """;
    }

    private static void EmitSyncRoutes(
        StringBuilder b,
        List<Route> syncRoutes,
        INamedTypeSymbol syncHandlerDefinition,
        INamedTypeSymbol syncVoidHandlerDefinition,
        INamedTypeSymbol syncBehaviorDefinition,
        INamedTypeSymbol syncVoidBehaviorDefinition)
    {
        for (var index = 0; index < syncRoutes.Count; index++)
        {
            var route = syncRoutes[index];
            var tp = TypeParameters(route);
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var resp = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var task = route.IsVoid ? "void" : resp;
            var cont = route.IsVoid ? $$"""global::Zendiator.ISyncRequestContinuation<{{req}}>""" : $$"""global::Zendiator.ISyncRequestContinuation<{{req}}, {{resp}}>""";
            var scoped = NeedsScoped(route) ? "scoped " : "";
            b.AppendLine($$"""
                    /// <summary>Dispatches the request synchronously without retaining it.</summary>
                    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                    public {{SyncSignature(route)}}
                    {
                """);
            Guard(b, route, "        ");
            b.Append("""        """);
            if (!route.IsVoid)
                b.Append("""return """);
            b.Append($$"""
                new SyncRoute{{index}}Node0{{tp}}(_services).Invoke(request, cancellationToken);
                    }
                """);
            for (var node = 0; node <= route.Behaviors.Count; node++)
            {
                b.AppendLine($$"""
                        private readonly struct SyncRoute{{index}}Node{{node}}{{TypeParameters(route)}}(global::Zendiator.DependencyInjection.ZendiatorServiceResolver services) : {{cont}}{{TypeConstraints(route)}}
                        {
                            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                            public {{task}} Invoke({{scoped}}{{req}} request, global::System.Threading.CancellationToken cancellationToken)
                            {
                    """);
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
                        contract = route.IsVoid ? $$"""global::Zendiator.ISyncRequestHandler<{{req}}>""" : $$"""global::Zendiator.ISyncRequestHandler<{{req}}, {{resp}}>""";
                    }

                    var direct = UseDirectCall(route.Handler, route.IsVoid ? syncVoidHandlerDefinition : syncHandlerDefinition, "Handle");
                    var recv = ServiceReceiver(handlerName, contract, direct);
                    b.Append("""            """);
                    if (!route.IsVoid)
                        b.Append("""return """);
                    b.AppendLine($$"""{{recv}}.Handle(request, cancellationToken);""");
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
                        contract = route.IsVoid ? $$"""global::Zendiator.ISyncPipelineBehavior<{{req}}>""" : $$"""global::Zendiator.ISyncPipelineBehavior<{{req}}, {{resp}}>""";
                    }

                    var direct = UseDirectCall(route.Behaviors[node], route.IsVoid ? syncVoidBehaviorDefinition : syncBehaviorDefinition, "Handle");
                    var recv = ServiceReceiver(behaviorName, contract, direct);
                    b.Append("""            """);
                    if (!route.IsVoid)
                        b.Append("""return """);
                    b.AppendLine($$"""
                        {{recv}}.Handle(request, new SyncRoute{{index}}Node{{node + 1}}{{TypeParameters(route)}}(services), cancellationToken);
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
