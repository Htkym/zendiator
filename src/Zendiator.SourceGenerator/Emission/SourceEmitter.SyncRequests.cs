using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private static bool NeedsScoped(EmissionRoute route) => route.Request.IsRefLikeType;
    private static bool NeedsScoped(EmissionMultiRoute route) => route.Request.IsRefLikeType;
    private static string SyncSignature(EmissionRoute route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var task = route.IsVoid ? "void" : (route.IsOpen ? route.ResponseDisplay : Name(route.Response));
        var tp = TypeParameters(route);
        var scoped = NeedsScoped(route) ? "scoped " : "";
        return $$"""
            {{task}} SendSync{{tp}}({{scoped}}{{req}} request, global::System.Threading.CancellationToken cancellationToken = default){{string.Concat(route.MethodConstraints)}}
            """;
    }

    private void EmitSyncRoutes(StringBuilder b)
    {
        var routes = _model.Routes.Synchronous.Single;
        for (var index = 0; index < routes.Count; index++)
            EmitSyncRequest(b, routes[index], index);
    }

    private void EmitSyncRequest(StringBuilder b, EmissionRoute route, int index)
    {
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
            new SyncRoute{{index}}Node0{{tp}}({{MediatorServices}}).Invoke(request, cancellationToken);
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

                var direct = route.Handler.DirectCall;
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

                var direct = route.Behaviors[node].DirectCall;
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
