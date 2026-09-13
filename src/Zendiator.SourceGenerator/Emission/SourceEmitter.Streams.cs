using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static string StreamSignature(Route route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var tp = TypeParameters(route);
        return $$"""
            global::System.Collections.Generic.IAsyncEnumerable<{{item}}> StreamAsync{{tp}}({{req}} request, global::System.Threading.CancellationToken cancellationToken = default){{string.Concat(route.MethodConstraints)}}
            """;
    }

    private static void EmitStreams(StringBuilder b, List<Route> streamRoutes, INamedTypeSymbol streamHandlerDefinition, INamedTypeSymbol streamBehaviorDefinition)
    {
        for (var index = 0; index < streamRoutes.Count; index++)
        {
            var route = streamRoutes[index];
            var tp = TypeParameters(route);
            var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
            var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
            var cont = $$"""global::Zendiator.IStreamContinuation<{{req}}, {{item}}>""";
            var enumerable = $$"""StreamRoute{{index}}Enumerable""";
            var enumerator = $$"""StreamRoute{{index}}Enumerator""";
            b.AppendLine($$"""
                    /// <summary>Streams items for the request through its configured pipeline. Enumeration is lazy and scope-safe.</summary>
                    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                    public {{StreamSignature(route)}}
                    {
                """);
            // Lazy: no handler execution, no DI resolution here. Only capture scope provider + request + token.
            b.AppendLine($$"""
                        return new {{enumerable}}{{tp}}(_services, request, cancellationToken);
                    }
                """);
            EmitStreamEnumerable(
                b,
                route,
                tp,
                req,
                item,
                enumerable,
                enumerator);
            EmitStreamEnumerator(
                b,
                route,
                index,
                tp,
                req,
                item,
                enumerator);
            for (var node = 0; node <= route.Behaviors.Count; node++)
            {
                b.AppendLine($$"""
                        private readonly struct StreamRoute{{index}}Node{{node}}{{TypeParameters(route)}}(global::Zendiator.DependencyInjection.ZendiatorServiceResolver services) : {{cont}}{{TypeConstraints(route)}}
                        {
                            [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                            public global::System.Collections.Generic.IAsyncEnumerable<{{item}}> InvokeAsync({{req}} request, global::System.Threading.CancellationToken cancellationToken)
                            {
                    """);
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
                        contract = $$"""global::Zendiator.IStreamRequestHandler<{{req}}, {{item}}>""";
                    }

                    var direct = UseDirectCall(route.Handler, streamHandlerDefinition);
                    var recv = ServiceReceiver(handlerName, contract, direct);
                    b.AppendLine($$"""            return {{recv}}.HandleAsync(request, cancellationToken);""");
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
                        contract = $$"""global::Zendiator.IStreamPipelineBehavior<{{req}}, {{item}}>""";
                    }

                    var direct = UseDirectCall(route.Behaviors[node], streamBehaviorDefinition);
                    var recv = ServiceReceiver(behaviorName, contract, direct);
                    b.AppendLine($$"""
                                    return {{recv}}.HandleAsync(request, new StreamRoute{{index}}Node{{node + 1}}{{TypeParameters(route)}}(services), cancellationToken);
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
