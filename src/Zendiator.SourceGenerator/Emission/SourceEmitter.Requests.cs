using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static string Signature(Route route)
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

    private static string HandlerContract(Route route) => route.IsVoid ? $$"""global::Zendiator.IRequestHandler<{{Name(route.Request)}}>""" : $$"""global::Zendiator.IRequestHandler<{{Name(route.Request)}}, {{Name(route.Response)}}>""";
    private static string BehaviorContract(Route route) => route.IsVoid ? $$"""global::Zendiator.IPipelineBehavior<{{Name(route.Request)}}>""" : $$"""global::Zendiator.IPipelineBehavior<{{Name(route.Request)}}, {{Name(route.Response)}}>""";
    private static string ContinuationContract(Route route)
    {
        if (route.IsOpen)
            return route.IsVoid ? $$"""global::Zendiator.IRequestContinuation<{{route.RequestDisplay}}>""" : $$"""global::Zendiator.IRequestContinuation<{{route.RequestDisplay}}, {{route.ResponseDisplay}}>""";
        return route.IsVoid ? $$"""global::Zendiator.IRequestContinuation<{{Name(route.Request)}}>""" : $$"""global::Zendiator.IRequestContinuation<{{Name(route.Request)}}, {{Name(route.Response)}}>""";
    }

    private static string TaskContract(Route route)
    {
        if (route.IsOpen)
            return route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $$"""global::System.Threading.Tasks.ValueTask<{{route.ResponseDisplay}}>""";
        return route.IsVoid ? "global::System.Threading.Tasks.ValueTask" : $$"""global::System.Threading.Tasks.ValueTask<{{Name(route.Response)}}>""";
    }

    private static void EmitRequests(
        StringBuilder b,
        List<Route> routes,
        INamedTypeSymbol handlerDefinition,
        INamedTypeSymbol behaviorDefinition,
        INamedTypeSymbol voidHandlerDefinition,
        INamedTypeSymbol voidBehaviorDefinition)
    {
        for (var index = 0; index < routes.Count; index++)
        {
            var route = routes[index];
            if (route.IsOpen)
            {
                EmitOpenRoute(
                    b,
                    route,
                    index,
                    handlerDefinition,
                    behaviorDefinition,
                    voidHandlerDefinition,
                    voidBehaviorDefinition);
                continue;
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
                var direct = UseDirectCall(route.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition);
                var receiver = $$"""_services.GetRequiredService<{{Name(route.Handler)}}>()""";
                if (!direct)
                    receiver = "((" + HandlerContract(route) + ")" + receiver + ")";
                b.AppendLine($$"""
                            return {{receiver}}.HandleAsync(request, cancellationToken);
                        }
                    """);
                continue;
            }

            b.AppendLine($$"""
                        return new Route{{index}}Node0(_services).InvokeAsync(request, cancellationToken);
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
                    if (UseDirectCall(route.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition))
                        HandlerCallDirect(b, route);
                    else
                        HandlerCall(b, route, "services", "            ");
                }
                else
                {
                    var receiver = $$"""services.GetRequiredService<{{Name(route.Behaviors[node])}}>()""";
                    if (!UseDirectCall(route.Behaviors[node], route.IsVoid ? voidBehaviorDefinition : behaviorDefinition))
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
}
