using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private static string MultiSignature(EmissionMultiRoute route)
    {
        if (route.IsOpen)
        {
            var tp = string.Join(", ", route.OpenTypeParams);
            var task = route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $$"""
                global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IReadOnlyList<{{route.ResponseDisplay}}>>
                """;
            return $$"""
                {{task}} SendAllAsync<{{tp}}>({{route.RequestDisplay}} request, global::System.Threading.CancellationToken cancellationToken = default){{string.Concat(route.MethodConstraints)}}
                """;
        }

        var closedTask = route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $$"""
            global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IReadOnlyList<{{Name(route.Response)}}>>
            """;
        return $$"""
            {{closedTask}} SendAllAsync({{Name(route.Request)}} request, global::System.Threading.CancellationToken cancellationToken = default)
            """;
    }

    private void EmitMulti(StringBuilder b)
    {
        var routes = _model.Routes.Requests.Multiple;
        for (var index = 0; index < routes.Count; index++)
            EmitMultiRequest(b, routes[index], index);
    }

    private void EmitMultiRequest(StringBuilder b, EmissionMultiRoute route, int index)
    {
        var tp = TypeParameters(route);
        var reqDisplay = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var respDisplay = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var contDisplay = route.IsVoid ? $$"""global::Zendiator.IRequestContinuation<{{reqDisplay}}>""" : $$"""global::Zendiator.IRequestContinuation<{{reqDisplay}}, {{respDisplay}}>""";
        var taskDisplay = route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $$"""global::System.Threading.Tasks.ValueTask<{{respDisplay}}>""";
        var branchContract = route.IsVoid ? $$"""global::Zendiator.IRequestHandler<{{reqDisplay}}>""" : $$"""global::Zendiator.IRequestHandler<{{reqDisplay}}, {{respDisplay}}>""";
        var behaviorRouteContract = route.IsVoid ? $$"""global::Zendiator.IPipelineBehavior<{{reqDisplay}}>""" : $$"""global::Zendiator.IPipelineBehavior<{{reqDisplay}}, {{respDisplay}}>""";
        b.AppendLine($$"""
                /// <summary>Dispatches the request to every handler in order.</summary>
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                public async {{MultiSignature(route)}}
                {
            """);
        if (route.Request.IsReferenceType)
            b.AppendLine("""        global::System.ArgumentNullException.ThrowIfNull(request);""");
        b.AppendLine("""        if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);""");
        if (!route.IsVoid)
            b.AppendLine($$"""
                        var results = new global::System.Collections.Generic.List<{{respDisplay}}>({{route.Branches.Count}});
                """);
        for (var bh = 0; bh < route.Branches.Count; bh++)
        {
            var node0 = $$"""MultiRoute{{index}}Branch{{bh}}Node0{{tp}}""";
            if (route.IsVoid)
                b.AppendLine($$"""
                            await new {{node0}}({{MediatorServices}}).InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                    """);
            else
                b.AppendLine($$"""
                            results.Add(await new {{node0}}({{MediatorServices}}).InvokeAsync(request, cancellationToken).ConfigureAwait(false));
                    """);
        }

        if (!route.IsVoid)
            b.AppendLine("""        return results;""");
        b.AppendLine("""    }""");
        for (var bh = 0; bh < route.Branches.Count; bh++)
        {
            var branch = route.Branches[bh];
            var prefix = $$"""MultiRoute{{index}}Branch{{bh}}""";
            for (var node = 0; node <= branch.Behaviors.Count; node++)
            {
                b.AppendLine($$"""
                        private readonly struct {{prefix}}Node{{node}}{{TypeParameters(route)}}(global::Zendiator.DependencyInjection.ZendiatorServiceResolver services) : {{contDisplay}}{{TypeConstraints(route)}}
                        {
                            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                            public {{taskDisplay}} InvokeAsync({{reqDisplay}} request, global::System.Threading.CancellationToken cancellationToken)
                            {
                    """);
                if (route.Request.IsReferenceType)
                    b.AppendLine("""            global::System.ArgumentNullException.ThrowIfNull(request);""");
                b.AppendLine("""            if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);""");
                if (node == branch.Behaviors.Count)
                {
                    var handlerName = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerDisplay : Name(branch.Handler);
                    var contract = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerContractDisplay : branchContract;
                    var direct = branch.Handler.DirectCall;
                    var recv = ServiceReceiver(handlerName, contract, direct);
                    b.AppendLine($$"""            return {{recv}}.HandleAsync(request, cancellationToken);""");
                }
                else
                {
                    var behaviorName = route.IsOpen ? branch.BehaviorDisplays[node] : Name(branch.Behaviors[node]);
                    var contract = route.IsOpen ? branch.BehaviorContractDisplays[node] : behaviorRouteContract;
                    var direct = branch.Behaviors[node].DirectCall;
                    var recv = ServiceReceiver(behaviorName, contract, direct);
                    b.AppendLine($$"""
                                    return {{recv}}.HandleAsync(request, new {{prefix}}Node{{node + 1}}{{TypeParameters(route)}}(services), cancellationToken);
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
