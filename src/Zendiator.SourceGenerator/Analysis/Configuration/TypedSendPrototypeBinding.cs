using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

// Explicitly invoked experimental frontend. Nothing in AddZendiator/discovery selects this protocol.
// Proposal contracts live in the experiment's consumer input, not the runtime package API.
internal static class TypedSendPrototypeBinding
{
    private const string Protocol = "Zendiator.SendTypeConnectionPrototype.";
    internal sealed record Result(TypedSendPrototypeModel? Model, Diagnostic? Diagnostic, bool CompilerOwnsError);

    internal static Result Bind(Compilation compilation, INamedTypeSymbol declaration)
    {
        var anchor = declaration.Locations.First();
        Result Reject(DiagnosticDescriptor descriptor, string message) => new(null, Diagnostic.Create(descriptor, anchor, message), false);
        var routeDefinition = compilation.GetTypeByMetadataName(Protocol + "IRoute`7");
        var routes = declaration.AllInterfaces.Where(i => Same(i.OriginalDefinition, routeDefinition)).ToArray();
        if (routes.Length != 1) return Reject(GeneratorDiagnostics.StageContractMismatch, "A prototype declaration requires one explicit seven-port route contract.");
        var args = routes[0].TypeArguments;
        if (args.Any(Error)) return new(null, null, true); // Roslyn owns unresolved source types, including nested arguments.
        var outer = args[0]; var inner = args[1]; var response = args[2]; var published = args[3];
        if (args.Skip(4).Any(t => t is not INamedTypeSymbol))
            return Reject(GeneratorDiagnostics.UnsupportedGenerationShape, "Prototype receivers must be named class services.");
        var handler = (INamedTypeSymbol)args[4]; var requestMap = (INamedTypeSymbol)args[5]; var responseMap = (INamedTypeSymbol)args[6];
        var assembly = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        INamedTypeSymbol[] Contracts(INamedTypeSymbol type, string name, bool framework = false)
        {
            var definition = framework ? assembly?.GetTypeByMetadataName(name) : compilation.GetTypeByMetadataName(Protocol + name);
            return type.AllInterfaces.Where(i => Same(i.OriginalDefinition, definition)).ToArray();
        }
        var handlers = Contracts(handler, ContractNames.SyncHandler, true);
        var requests = Contracts(requestMap, "IRequestMap`2");
        var responses = Contracts(responseMap, "IResponseMap`3");
        if (handlers.Length != 1 || requests.Length != 1 || responses.Length != 1)
            return Reject(GeneratorDiagnostics.StageContractMismatch, "Prototype services require one synchronous handler, request-map and response-map contract each.");
        var h = handlers[0]; var before = requests[0]; var after = responses[0];
        bool Equal(ITypeSymbol a, ITypeSymbol b) => SymbolEqualityComparer.IncludeNullability.Equals(a, b);
        if (!Equal(h.TypeArguments[0], inner) || !Equal(h.TypeArguments[1], response))
            return Reject(GeneratorDiagnostics.StageTypeMismatch, "The explicit inner handler request/response ports do not match the handler contract, including nullability.");
        if (!Equal(before.TypeArguments[0], outer) || !Equal(before.TypeArguments[1], inner))
            return Reject(GeneratorDiagnostics.StageTypeMismatch, "The request-map input/output ports do not match the external/handler request, including nullability.");
        if (!Equal(after.TypeArguments[0], inner) || !Equal(after.TypeArguments[1], response) || !Equal(after.TypeArguments[2], published))
            return Reject(GeneratorDiagnostics.StageTypeMismatch, "The response-map observed request/input/public response ports differ, including nullability.");
        var cancellation = compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        bool MapSignature(INamedTypeSymbol contract, ITypeSymbol result, params ITypeSymbol[] inputs)
        {
            var methods = contract.GetMembers().OfType<IMethodSymbol>().ToArray();
            return methods.Length == 1 && !methods[0].IsStatic && methods[0].Arity == 0 && Equal(methods[0].ReturnType, result) &&
                methods[0].Parameters.Length == inputs.Length + 1 && methods[0].Parameters.All(p => p.RefKind == RefKind.None) &&
                methods[0].Parameters.Take(inputs.Length).Select(p => p.Type).Zip(inputs, Equal).All(x => x) &&
                Same(methods[0].Parameters.Last().Type, cancellation);
        }
        if (!MapSignature(before, inner, outer) || !MapSignature(after, published, inner, response))
            return Reject(GeneratorDiagnostics.StageContractMismatch, "The proposal map signatures must match their explicit input/output ports and CancellationToken.");
        var outerRequests = outer is INamedTypeSymbol o ? Contracts(o, ContractNames.SyncRequest, true) : System.Array.Empty<INamedTypeSymbol>();
        if (outerRequests.Length != 1 || !Equal(outerRequests[0].TypeArguments[0], published))
            return Reject(GeneratorDiagnostics.StageTypeMismatch, "The external request's declared public response contract must match the explicit public response port.");
        var ports = new[] { outer, inner, response, published };
        if (!Class(outer) || !Class(inner) || ports.Any(t => t.IsRefLikeType || MarkedHandlerDiscovery.HasOpenShape(t) || !Public(t)) ||
            new[] { handler, requestMap, responseMap }.Any(t => !Class(t)) || response.SpecialType != SpecialType.System_Int32 || published.SpecialType != SpecialType.System_String)
            return Reject(GeneratorDiagnostics.UnsupportedGenerationShape, "This prototype requires nonnull closed class requests and borrowed closed class receivers, one int handler response and a string public response; other shapes need separate proofs.");
        var marker = assembly?.GetTypeByMetadataName("Zendiator.ZendiatorHandlerAttribute");
        if (!handler.GetAttributes().Any(a => Same(a.AttributeClass, marker)))
            return Reject(GeneratorDiagnostics.UnsupportedGenerationShape, "The explicitly supplied prototype handler requires ZendiatorHandler.");
        INamedTypeSymbol? Observer(INamedTypeSymbol receiver, string name, ITypeSymbol saved) =>
            Contracts(receiver, name, true).SingleOrDefault(i => Equal(i.TypeArguments[0], saved));
        var outerException = Observer(requestMap, "Zendiator.ISendExceptionObserver`1", outer);
        var outerFinally = Observer(requestMap, "Zendiator.ISendFinallyObserver`1", outer);
        var innerException = Observer(responseMap, "Zendiator.ISendExceptionObserver`1", inner);
        var innerFinally = Observer(responseMap, "Zendiator.ISendFinallyObserver`1", inner);
        if (outerException == null || outerFinally == null || innerException == null || innerFinally == null)
            return Reject(GeneratorDiagnostics.StageContractMismatch, "Each entered map stage requires Exception/Finally for its own saved request type and nullability.");
        var bindings = new[] { (handler, h), (requestMap, before), (responseMap, after), (requestMap, outerException),
            (requestMap, outerFinally), (responseMap, innerException), (responseMap, innerFinally) };
        if (bindings.Any(pair => pair.Item2.GetMembers().OfType<IMethodSymbol>().Count() != 1 ||
            pair.Item1.FindImplementationForInterfaceMember(pair.Item2.GetMembers().OfType<IMethodSymbol>().Single()) is not IMethodSymbol))
            return new(null, null, true); // Missing or invalid implementations stay compiler-owned.
        string Name(ITypeSymbol t) => CanonicalSymbolName.Display(t, compilation);
        TypedSendPort Port(ITypeSymbol t) => new(Name(t) + "|assembly=" + t.ContainingAssembly?.Identity + "|nullable=" + t.NullableAnnotation,
            Name(t), t.NullableAnnotation.ToString(), t.IsReferenceType);
        TypedSendCall Call(INamedTypeSymbol contract) => new(Name(contract), contract.GetMembers().OfType<IMethodSymbol>().Single().Name);
        var external = Port(outer); var internalRequest = Port(inner); var handlerResult = Port(response); var publicResult = Port(published);
        return new(new("__ZendiatorTypedSend_" + declaration.Name, external, publicResult, internalRequest, handlerResult,
            Name(handler), Call(h), new(Name(requestMap), external, external, internalRequest, Call(before), Call(outerException), Call(outerFinally)),
            new(Name(responseMap), internalRequest, handlerResult, publicResult, Call(after), Call(innerException), Call(innerFinally))), null, false);
    }

    private static bool Class(ITypeSymbol t) => t is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false, IsStatic: false } &&
        t.NullableAnnotation != NullableAnnotation.Annotated && !MarkedHandlerDiscovery.HasOpenShape(t) && Public(t);
    private static bool Error(ITypeSymbol t) => t.TypeKind == TypeKind.Error || t is IArrayTypeSymbol a && Error(a.ElementType) ||
        t is INamedTypeSymbol n && (n.TypeArguments.Any(Error) || n.ContainingType != null && Error(n.ContainingType));
}
