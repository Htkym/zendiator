using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private void EmitSyncMulti(StringBuilder b)
    {
        var routes = _model.Routes.Synchronous.Multiple;
        for (var index = 0; index < routes.Count; index++)
            EmitSyncMultiRequest(b, routes[index], index);
    }

    private void EmitSyncMultiRequest(StringBuilder b, EmissionMultiRoute route, int index)
    {
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
        b.AppendLine("""        if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);""");
        if (!route.IsVoid)
            b.AppendLine($$"""
                        var results = CreateResultArray<{{resp}}>({{route.Branches.Count}});
                """);
        for (var bh = 0; bh < route.Branches.Count; bh++)
        {
            var node0 = $$"""SyncMultiRoute{{index}}Branch{{bh}}Node0{{tp}}""";
            if (route.IsVoid)
                b.AppendLine($$"""        new {{node0}}({{MediatorServices}}).Invoke(request, cancellationToken);""");
            else
                b.AppendLine($$"""        results[{{bh}}] = new {{node0}}({{MediatorServices}}).Invoke(request, cancellationToken);""");
        }

        if (!route.IsVoid)
            b.AppendLine("""        return results;""");
        b.AppendLine("""    }""");
        if (!route.IsVoid)
        {
            b.AppendLine($$"""
                    /// <summary>Writes all results in handler order after every handler succeeds. Returns the number written.</summary>
                    /// <exception cref="global::System.ArgumentException">The destination is too short; no handler is invoked.</exception>
                    public {{SyncMultiSpanSignature(route)}}
                    {
                """);
            if (route.Request.IsReferenceType)
                b.AppendLine("""        global::System.ArgumentNullException.ThrowIfNull(request);""");
            b.AppendLine($$"""
                        if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);
                        if (destination.Length < {{route.Branches.Count}}) throw new global::System.ArgumentException("The destination must hold at least {{route.Branches.Count}} results.", nameof(destination));
                """);
            for (var bh = 0; bh < route.Branches.Count; bh++)
                b.AppendLine($$"""        var result{{bh}} = new SyncMultiRoute{{index}}Branch{{bh}}Node0{{tp}}({{MediatorServices}}).Invoke(request, cancellationToken);""");
            for (var bh = 0; bh < route.Branches.Count; bh++)
                b.AppendLine($$"""        destination[{{bh}}] = result{{bh}};""");
            b.AppendLine($$"""
                        return {{route.Branches.Count}};
                    }
                """);
        }
        var set = SetOf("SyncMultiRoute" + index);
        for (var bh = 0; bh < route.Branches.Count; bh++)
        {
            var branch = route.Branches[bh];
            var prefix = $$"""SyncMultiRoute{{index}}Branch{{bh}}""";
            var branchContract = route.IsVoid ? $$"""global::Zendiator.ISyncRequestHandler<{{req}}>""" : $$"""global::Zendiator.ISyncRequestHandler<{{req}}, {{resp}}>""";
            var behaviorRouteContract = route.IsVoid ? $$"""global::Zendiator.ISyncPipelineBehavior<{{req}}>""" : $$"""global::Zendiator.ISyncPipelineBehavior<{{req}}, {{resp}}>""";
            for (var node = 0; node <= branch.Behaviors.Count; node++)
            {
                b.AppendLine($$"""
                        private readonly struct {{prefix}}Node{{node}}{{TypeParameters(route)}}({{NodeParameter(set, node)}}) : {{cont}}{{TypeConstraints(route)}}
                        {
                            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                            public {{(route.IsVoid ? "void" : resp)}} Invoke({{scoped}}{{req}} request, global::System.Threading.CancellationToken cancellationToken)
                            {
                    """);
                if (route.Request.IsReferenceType)
                    b.AppendLine("""            global::System.ArgumentNullException.ThrowIfNull(request);""");
                b.AppendLine("""            if (cancellationToken.IsCancellationRequested) ThrowDispatchCancellation(cancellationToken);""");
                EmitSetLookup(b, set, node, "            ");
                var services = NodeServices(set, node);
                if (node == branch.Behaviors.Count)
                {
                    var handlerName = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerDisplay : Name(branch.Handler);
                    var contract = route.IsOpen || branch.HandlerIsOpen ? branch.HandlerContractDisplay : branchContract;
                    var direct = branch.Handler.DirectCall;
                    var recv = Receiver(set, branch.Handler, handlerName, contract, direct, services);
                    b.Append("""            """);
                    if (!route.IsVoid)
                        b.Append("""return """);
                    b.AppendLine($$"""{{recv}}.Handle(request, cancellationToken);""");
                }
                else
                {
                    var behaviorName = route.IsOpen ? branch.BehaviorDisplays[node] : Name(branch.Behaviors[node]);
                    var contract = route.IsOpen ? branch.BehaviorContractDisplays[node] : behaviorRouteContract;
                    var direct = branch.Behaviors[node].DirectCall;
                    var recv = Receiver(set, branch.Behaviors[node], behaviorName, contract, direct, services);
                    b.Append("""            """);
                    if (!route.IsVoid)
                        b.Append("""return """);
                    b.AppendLine($$"""
                        {{recv}}.Handle(request, new {{prefix}}Node{{node + 1}}{{TypeParameters(route)}}({{NodeArgument(set)}}), cancellationToken);
                        """);
                }

                b.AppendLine("""
                            }
                        }
                    """);
            }
        }
    }

    private static string SyncMultiSignature(EmissionMultiRoute route)
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

    private static string SyncMultiSpanSignature(EmissionMultiRoute route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var resp = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var scoped = NeedsScoped(route) ? "scoped " : "";
        return $$"""
            int SendAllSync{{TypeParameters(route)}}({{scoped}}{{req}} request, scoped global::System.Span<{{resp}}> destination, global::System.Threading.CancellationToken cancellationToken){{string.Concat(route.MethodConstraints)}}
            """;
    }
}
