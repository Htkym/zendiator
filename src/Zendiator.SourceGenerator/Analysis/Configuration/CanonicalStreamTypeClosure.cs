using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.MarkedHandlerDiscovery;

namespace Zendiator.SourceGenerator;

// Binds supplied services to a known request/item shape. A validated open pattern
// may retain its own parameters; public signatures and callback bodies are not inferred here.
internal static class CanonicalStreamTypeClosure
{
    internal sealed record Result(INamedTypeSymbol? Type, string? Error);
    internal static Result SendHandler(INamedTypeSymbol type, INamedTypeSymbol request,
        Compilation compilation, CanonicalStreamOpenBinding open, bool async) => Close(type, request, null,
        async ? new[] { ContractNames.Handler, ContractNames.VoidHandler } : new[] { ContractNames.SyncHandler, ContractNames.SyncVoidHandler }, compilation, open, requestOnly: true);

    internal static Result SendHook(INamedTypeSymbol type, INamedTypeSymbol request,
        Compilation compilation, CanonicalStreamOpenBinding open, bool async) => Close(type, request, null,
        async ? new[] { "Zendiator.IAsyncSendRequestTransform`1", "Zendiator.IAsyncSendResponseTransform`2", "Zendiator.IAsyncSendSuccessObserver`1",
            "Zendiator.IAsyncSendExceptionObserver`1", "Zendiator.IAsyncSendFinallyObserver`1" } :
        new[] { "Zendiator.ISendRequestTransform`1", "Zendiator.ISendResponseTransform`2", "Zendiator.ISendSuccessObserver`1",
            "Zendiator.ISendExceptionObserver`1", "Zendiator.ISendFinallyObserver`1" }, compilation, open, requestOnly: true);
    internal static Result Handler(INamedTypeSymbol type, INamedTypeSymbol request, Compilation compilation, CanonicalStreamOpenBinding? open = null)
    {
        var assembly = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        var itemContract = assembly?.GetTypeByMetadataName(ContractNames.StreamRequest);
        var items = request.AllInterfaces.Where(i => Same(i.OriginalDefinition, itemContract)).Select(i => i.TypeArguments[0]).ToArray();
        if (items.Length != 1) return new(null, "A Stream request requires one item contract before closing its handler.");
        return Close(type, request, items[0], new[] { ContractNames.StreamHandler }, compilation, open);
    }
    internal static Result Stage(INamedTypeSymbol type, INamedTypeSymbol request, ITypeSymbol item,
        FusionStageKind kind, Compilation compilation, CanonicalStreamOpenBinding? open = null) => Close(type, request, item,
        kind == FusionStageKind.Wrapper ? new[] { "Zendiator.IStreamWrapper`2" } :
            new[] { "Zendiator.IStreamItemFilter`2", "Zendiator.IStreamItemMap`2",
                "Zendiator.IAsyncStreamItemFilter`2", "Zendiator.IAsyncStreamItemMap`2",
                "Zendiator.IStreamItemMap`3", "Zendiator.IAsyncStreamItemMap`3" }, compilation, open);

    internal static Result Factory(INamedTypeSymbol type, INamedTypeSymbol request, INamedTypeSymbol receiver,
        Compilation compilation, CanonicalStreamOpenBinding? open = null) => Close(type, request, receiver,
        new[] { "Zendiator.IOwnedSessionFactory`2", "Zendiator.IBorrowedSessionFactory`2", "Zendiator.IAsyncOwnedSessionFactory`2", "Zendiator.IAsyncBorrowedSessionFactory`2" }, compilation, open);

    internal static ITypeSymbol? Final(ITypeSymbol pattern, ITypeSymbol output, Compilation compilation, CanonicalStreamOpenBinding? open)
    {
        if (pattern is not INamedTypeSymbol { IsUnboundGenericType: true } unbound) return pattern;
        var definition = unbound.OriginalDefinition;
        var map = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        if (HasGenericContainer(definition) || !Match(definition, output, definition, map, compilation) || definition.TypeParameters.Any(p => !map.ContainsKey(p))) return null;
        var args = definition.TypeParameters.Select(p => map[p]).ToArray();
        if (open == null && !GenericConstraints.SatisfiesConstraints(definition, args, compilation)) return null;
        var closed = (INamedTypeSymbol)definition.Construct(args).WithNullableAnnotation(output.NullableAnnotation);
        return open != null && open.ApplyConstraints(closed) != null ? null : closed;
    }

    private static Result Close(INamedTypeSymbol type, INamedTypeSymbol request, ITypeSymbol? item,
        string[] names, Compilation compilation, CanonicalStreamOpenBinding? open, bool requestOnly = false)
    {
        if (!IsOpenDefinition(type))
        {
            if (open != null)
            {
                var error = open.ApplyConstraints(type);
                return error == null ? new(type, null) : new(null, error);
            }
            if (HasOpenShape(type)) return new(null, "Partially open Stream services require a closed route binding.");
            if (!Constraints(type, compilation)) return new(null, "The closed Stream service does not satisfy its generic constraints.");
            return new(type, null);
        }
        var definition = type.OriginalDefinition;
        if (HasGenericContainer(definition)) return new(null, "An open containing type cannot be closed by this Stream route backend.");
        var assembly = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        var contracts = names.Select(n => assembly?.GetTypeByMetadataName(n)).Where(d => d != null).ToArray();
        var candidates = new List<INamedTypeSymbol>(); bool constraintFailure = false, missingArgument = false;
        foreach (var contract in definition.AllInterfaces.Where(i => contracts.Any(d => Same(d, i.OriginalDefinition))))
        {
            var map = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
            if (!Match(contract.TypeArguments[0], request, definition, map, compilation) ||
                (!requestOnly && !Match(contract.TypeArguments[1], item!, definition, map, compilation))) continue;
            if (definition.TypeParameters.Any(p => !map.ContainsKey(p))) { missingArgument = true; continue; }
            var args = definition.TypeParameters.Select(p => map[p]).ToArray();
            if (open == null && (args.Any(HasOpenArguments) || !GenericConstraints.SatisfiesConstraints(definition, args, compilation)))
            { constraintFailure = true; continue; }
            var closed = definition.Construct(args);
            if (!candidates.Any(c => Same(c, closed))) candidates.Add(closed);
        }
        if (candidates.Count == 1 && open != null)
        {
            var error = open.ApplyConstraints(candidates[0]);
            return error == null ? new(candidates[0], null) : new(null, error);
        }
        return candidates.Count == 1 ? new(candidates[0], null) : new(null, candidates.Count > 1
            ? "The Stream service has more than one possible request/item type closure. Supply a closed service type."
            : constraintFailure ? "Inferred Stream service type arguments do not satisfy its generic constraints."
            : missingArgument ? "Not every Stream service type argument is determined by its request/item contract. Supply a closed service type."
            : "The open Stream service cannot be closed from this route's request/item contract.");
    }
    private static bool Constraints(INamedTypeSymbol type, Compilation compilation)
    {
        for (var current = type; current != null; current = current.ContainingType)
            if (current.Arity != 0 && !GenericConstraints.SatisfiesConstraints(current.OriginalDefinition, current.TypeArguments.ToArray(), compilation)) return false;
        return true;
    }
    private static bool Match(ITypeSymbol pattern, ITypeSymbol actual, INamedTypeSymbol owner,
        Dictionary<ITypeParameterSymbol, ITypeSymbol> map, Compilation compilation)
    {
        if (pattern is ITypeParameterSymbol parameter && Same(parameter.ContainingSymbol, owner))
        {
            if (map.TryGetValue(parameter, out var prior)) return RegistrationIdentity(prior, compilation) == RegistrationIdentity(actual, compilation);
            map.Add(parameter, actual); return true;
        }
        if (pattern is IArrayTypeSymbol array && actual is IArrayTypeSymbol otherArray)
            return array.Rank == otherArray.Rank && Match(array.ElementType, otherArray.ElementType, owner, map, compilation);
        if (pattern is INamedTypeSymbol named && actual is INamedTypeSymbol other && Same(named.OriginalDefinition, other.OriginalDefinition))
        {
            if ((named.ContainingType == null) != (other.ContainingType == null) || named.TypeArguments.Length != other.TypeArguments.Length) return false;
            if (named.ContainingType != null && !Match(named.ContainingType, other.ContainingType!, owner, map, compilation)) return false;
            for (var i = 0; i < named.TypeArguments.Length; i++) if (!Match(named.TypeArguments[i], other.TypeArguments[i], owner, map, compilation)) return false;
            return true;
        }
        return RegistrationIdentity(pattern, compilation) == RegistrationIdentity(actual, compilation);
    }
}
