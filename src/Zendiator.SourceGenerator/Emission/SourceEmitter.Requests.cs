using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private static string Signature(EmissionRoute route)
    {
        if (route.IsOpen)
        {
            var tp = string.Join(", ", route.OpenTypeParams);
            var task = route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $$"""global::System.Threading.Tasks.ValueTask<{{route.ResponseDisplay}}>""";
            return $$"""
                {{task}} SendAsync<{{tp}}>({{route.RequestDisplay}} request, global::System.Threading.CancellationToken cancellationToken = default){{string.Concat(route.MethodConstraints)}}
                """;
        }

        return route.IsVoid ? $$"""
            global::System.Threading.Tasks.ValueTask SendAsync({{Name(route.Request)}} request, global::System.Threading.CancellationToken cancellationToken = default)
            """ : $$"""
            global::System.Threading.Tasks.ValueTask<{{Name(route.Response)}}> SendAsync({{Name(route.Request)}} request, global::System.Threading.CancellationToken cancellationToken = default)
            """;
    }

    private static string HandlerContract(EmissionRoute route) => route.IsVoid ? $$"""global::Zendiator.IRequestHandler<{{Name(route.Request)}}>""" : $$"""global::Zendiator.IRequestHandler<{{Name(route.Request)}}, {{Name(route.Response)}}>""";
    private static string BehaviorContract(EmissionRoute route) => route.IsVoid ? $$"""global::Zendiator.IPipelineBehavior<{{Name(route.Request)}}>""" : $$"""global::Zendiator.IPipelineBehavior<{{Name(route.Request)}}, {{Name(route.Response)}}>""";
    private static string ContinuationContract(EmissionRoute route)
    {
        if (route.IsOpen)
            return route.IsVoid ? $$"""global::Zendiator.IRequestContinuation<{{route.RequestDisplay}}>""" : $$"""global::Zendiator.IRequestContinuation<{{route.RequestDisplay}}, {{route.ResponseDisplay}}>""";
        return route.IsVoid ? $$"""global::Zendiator.IRequestContinuation<{{Name(route.Request)}}>""" : $$"""global::Zendiator.IRequestContinuation<{{Name(route.Request)}}, {{Name(route.Response)}}>""";
    }

    private static string TaskContract(EmissionRoute route)
    {
        if (route.IsOpen)
            return route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $$"""global::System.Threading.Tasks.ValueTask<{{route.ResponseDisplay}}>""";
        return route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $$"""global::System.Threading.Tasks.ValueTask<{{Name(route.Response)}}>""";
    }

    private void EmitRequests(StringBuilder b)
    {
        var routes = _model.Routes.Requests.Single;
        for (var index = 0; index < routes.Count; index++)
            EmitRequest(b, routes[index], index);
    }

    private void EmitRequest(StringBuilder b, EmissionRoute route, int index)
    {
        if (route.IsOpen)
        {
            EmitOpenRoute(b, route, index);
            return;
        }

        b.AppendLine($$"""
                /// <summary>Dispatches the request through its configured pipeline.</summary>
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                public {{Signature(route)}}
                {
            """);
        if (route.Behaviors.Count == 0)
        {
            Guard(b, route, "        ");
            var direct = route.Handler.DirectCall;
            var receiver = _singleServiceTypeName != null
                ? $$"""{{MediatorServices}}.GetRequiredService()"""
                : $$"""{{MediatorServices}}.GetRequiredService<{{Name(route.Handler)}}>()""";
            if (!direct)
                receiver = "((" + HandlerContract(route) + ")" + receiver + ")";
            b.AppendLine($$"""
                        return {{receiver}}.HandleAsync(request, cancellationToken);
                    }
                """);
            return;
        }

        b.AppendLine($$"""
                    return new Route{{index}}Node0({{MediatorServices}}).InvokeAsync(request, cancellationToken);
                }
            """);
        for (var node = 0; node <= route.Behaviors.Count; node++)
        {
            b.AppendLine($$"""
                    private readonly struct Route{{index}}Node{{node}}(global::Zendiator.DependencyInjection.ZendiatorServiceResolver services) : {{ContinuationContract(route)}}
                    {
                        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                        public {{TaskContract(route)}} InvokeAsync({{Name(route.Request)}} request, global::System.Threading.CancellationToken cancellationToken)
                        {
                """);
            Guard(b, route, "            ");
            if (node == route.Behaviors.Count)
            {
                if (route.Handler.DirectCall)
                    HandlerCallDirect(b, route);
                else
                    HandlerCall(b, route, "services", "            ");
            }
            else
            {
                var receiver = $$"""services.GetRequiredService<{{Name(route.Behaviors[node])}}>()""";
                if (!route.Behaviors[node].DirectCall)
                    receiver = "((" + BehaviorContract(route) + ")" + receiver + ")";
                b.AppendLine($$"""
                                return {{receiver}}.HandleAsync(request, new Route{{index}}Node{{node + 1}}(services), cancellationToken);
                    """);
            }

            b.AppendLine("""
                        }
                    }
                """);
        }
    }
}
