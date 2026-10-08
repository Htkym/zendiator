using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private static string StreamSignature(EmissionRoute route)
    {
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var tp = TypeParameters(route);
        return $$"""
            global::System.Collections.Generic.IAsyncEnumerable<{{item}}> StreamAsync{{tp}}({{req}} request, global::System.Threading.CancellationToken cancellationToken = default){{string.Concat(route.MethodConstraints)}}
            """;
    }

    private void EmitStreams(StringBuilder b)
    {
        var routes = _model.Routes.Streams;
        if (routes.Count != 0)
            b.AppendLine("""
                    // Forwarding delegates subsequent completion/failure to the inner enumerator.
                    // Stopped prevents forwarding; it does not imply that resources are disposed.
                    private enum StreamEnumeratorState : byte { Unclaimed, NotStarted, Forwarding, Stopped }
                """);
        for (var index = 0; index < routes.Count; index++)
            EmitStream(b, routes[index], index);
    }

    private void EmitStream(StringBuilder b, EmissionRoute route, int index)
    {
        var tp = TypeParameters(route);
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var cont = $$"""global::Zendiator.IStreamContinuation<{{req}}, {{item}}>""";
        var enumerable = $$"""StreamRoute{{index}}Enumerable""";
        var set = SetOf("StreamRoute" + index);
        b.AppendLine($$"""
                /// <summary>Streams items for the request through its configured pipeline. Enumeration is lazy and scope-safe.</summary>
                [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                public {{StreamSignature(route)}}
                {
            """);
        if (route.Validators.Count != 0)
        {
            if (route.Request.IsReferenceType)
                b.AppendLine("""        global::System.ArgumentNullException.ThrowIfNull(request);""");
            var acquiredSet = false;
            foreach (var validator in route.Validators)
            {
                if (!acquiredSet && (set?.IndexOf(validator) ?? -1) >= 0)
                {
                    b.AppendLine($"        var dependencies = {set!.Name}.GetForValidation({MediatorServices});");
                    acquiredSet = true;
                }
                var contract = $"global::Zendiator.IStreamRequestValidator<{req}>";
                var receiver = Receiver(set, validator, Name(validator), contract, validator.DirectCall, MediatorServices);
                b.AppendLine($"        {receiver}.Validate(request);");
            }
        }
        // Validation is synchronous; handler and behavior execution stays lazy.
        b.AppendLine($$"""
                    return new {{enumerable}}{{tp}}({{MediatorServices}}, request, cancellationToken);
                }
            """);
        EmitStreamEnumerable(b, route, index);
        for (var node = 0; node <= route.Behaviors.Count; node++)
        {
            b.AppendLine($$"""
                    private readonly struct StreamRoute{{index}}Node{{node}}{{TypeParameters(route)}}({{NodeParameter(set, node)}}) : {{cont}}{{TypeConstraints(route)}}
                    {
                        [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
                        public global::System.Collections.Generic.IAsyncEnumerable<{{item}}> InvokeAsync({{req}} request, global::System.Threading.CancellationToken cancellationToken)
                        {
                """);
            EmitSetLookup(b, set, node, "            ");
            var services = NodeServices(set, node);
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

                var direct = route.Handler.DirectCall;
                var recv = Receiver(set, route.Handler, handlerName, contract, direct, services);
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

                var direct = route.Behaviors[node].DirectCall;
                var recv = Receiver(set, route.Behaviors[node], behaviorName, contract, direct, services);
                b.AppendLine($$"""
                                return {{recv}}.HandleAsync(request, new StreamRoute{{index}}Node{{node + 1}}{{TypeParameters(route)}}({{NodeArgument(set)}}), cancellationToken);
                    """);
            }

            b.AppendLine("""
                        }
                    }
                """);
        }
    }
}
