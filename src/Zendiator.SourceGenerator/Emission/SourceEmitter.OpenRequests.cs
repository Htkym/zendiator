using System.Text;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

internal static partial class SourceEmitter
{
    private static void EmitOpenRoute(
        StringBuilder b,
        Route route,
        int index,
        INamedTypeSymbol handlerDefinition,
        INamedTypeSymbol behaviorDefinition,
        INamedTypeSymbol voidHandlerDefinition,
        INamedTypeSymbol voidBehaviorDefinition)
    {
        // Each closed service type has its own lazy dependency slot on the mediator.
        var tp = string.Join(", ", route.OpenTypeParams);
        b.AppendLine($$"""
                /// <summary>Dispatches the request through its configured pipeline.</summary>
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                public {{Signature(route)}}
                {
            """);
        Guard(b, route, "        ");
        b.Append($$"""
                    return new OpenRoute{{index}}Node0<{{tp}}>(_services).InvokeAsync(request, cancellationToken);
                }
            """);
        for (var node = 0; node <= route.Behaviors.Count; node++)
        {
            b.AppendLine($$"""
                    private readonly struct OpenRoute{{index}}Node{{node}}<{{tp}}>(global::Zendiator.DependencyInjection.ZendiatorServiceResolver services) : {{ContinuationContract(route)}}{{string.Concat(route.MethodConstraints)}}
                    {
                        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                        public {{TaskContract(route)}} InvokeAsync({{route.RequestDisplay}} request, global::System.Threading.CancellationToken cancellationToken)
                        {
                """);
            Guard(b, route, "            ");
            if (node == route.Behaviors.Count)
            {
                var directH = UseDirectCall(route.Handler, route.IsVoid ? voidHandlerDefinition : handlerDefinition);
                b.AppendLine($$"""
                                return {{(ServiceReceiver(route.HandlerDisplay, route.HandlerContractDisplay, directH))}}.HandleAsync(request, cancellationToken);
                    """);
            }
            else
            {
                var directB = UseDirectCall(route.Behaviors[node], route.IsVoid ? voidBehaviorDefinition : behaviorDefinition);
                b.Append($$"""
                                return {{(ServiceReceiver(route.BehaviorDisplays[node], route.BehaviorContractDisplays[node], directB))}}.HandleAsync(request, new OpenRoute{{index}}Node{{node + 1}}<{{tp}}>(services), cancellationToken);
                    """);
            }

            b.AppendLine("""
                        }
                    }
                """);
        }
    }
}
