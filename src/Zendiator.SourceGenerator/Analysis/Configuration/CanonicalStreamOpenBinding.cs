using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.MarkedHandlerDiscovery;

namespace Zendiator.SourceGenerator;

// An open request is a handler-derived pattern, not an arbitrary scan of request types.
// This object is local to one frontend binding; only its value projection survives.
internal sealed class CanonicalStreamOpenBinding
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _owner;
    private readonly Dictionary<ITypeParameterSymbol, string> _names = new(SymbolEqualityComparer.Default);
    private readonly MergedParam[] _constraints;
    private ITypeSymbol? _sendResponse;
    internal INamedTypeSymbol Request { get; }
    private CanonicalStreamOpenBinding(Compilation compilation, INamedTypeSymbol owner, INamedTypeSymbol request)
    {
        (_compilation, _owner, Request) = (compilation, owner, request);
        _constraints = owner.TypeParameters.Select((p, i) =>
        {
            var name = "__ZendiatorType" + i;
            _names.Add(p, name);
            var value = new MergedParam(name);
            GenericConstraints.MergeHandlerParam(value, p, owner, owner.TypeParameters.Cast<ITypeSymbol>().ToArray(), compilation);
            return value;
        }).ToArray();
    }
    internal static CanonicalStreamOpenBinding? Create(INamedTypeSymbol definition, INamedTypeSymbol handler,
        Compilation compilation, out string? error)
    {
        error = null;
        handler = handler.OriginalDefinition;
        var contract = compilation.GetTypeByMetadataName(ContractNames.StreamHandler);
        var matches = handler.AllInterfaces.Where(i => Same(i.OriginalDefinition, contract) &&
            i.TypeArguments[0] is INamedTypeSymbol request && Same(request.OriginalDefinition, definition.OriginalDefinition)).ToArray();
        if (matches.Length != 1 || !IsOpenDefinition(handler) || HasGenericContainer(handler) || HasGenericContainer(definition))
        { error = "An open Stream request requires one selected generic handler contract and no open containing type."; return null; }
        var pattern = (INamedTypeSymbol)matches[0].TypeArguments[0];
        var binding = new CanonicalStreamOpenBinding(compilation, handler, pattern);
        var used = Parameters(pattern).ToArray();
        if (handler.TypeParameters.Any(p => !used.Any(u => Same(p, u))) || !binding.Contains(pattern) ||
            handler.TypeParameters.Any(p => p.AllowsRefLikeType))
        { error = "Every handler type parameter must be determined by the request pattern; foreign or ref-like parameters are unsupported."; return null; }
        if (!binding.Public(pattern) || pattern.IsRefLikeType || !binding.Public(matches[0].TypeArguments[1]))
        { error = "An open Stream request/item pattern requires public type leaves and ownable type parameters."; return null; }
        var itemContract = compilation.GetTypeByMetadataName(ContractNames.StreamRequest);
        var items = pattern.AllInterfaces.Where(i => Same(i.OriginalDefinition, itemContract)).ToArray();
        if (items.Length != 1 || RegistrationIdentity(items[0].TypeArguments[0], compilation) != RegistrationIdentity(matches[0].TypeArguments[1], compilation))
        { error = "The open handler item contract must match the request item pattern exactly."; return null; }
        error = binding.ApplyConstraints(pattern);
        return error == null ? binding : null;
    }
    internal string Display(ITypeSymbol type) => CanonicalSymbolName.Display(type, _compilation, _names);
    // Share symbol projection and constraint merging, not Stream's request/item contract.
    internal static CanonicalStreamOpenBinding? CreateSend(INamedTypeSymbol definition, INamedTypeSymbol handler,
        Compilation compilation, bool async, out string? error, bool allowReferenceResponse = false)
    {
        error = null;
        handler = handler.OriginalDefinition;
        var assembly = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        var contracts = (async ? new[] { ContractNames.Handler, ContractNames.VoidHandler } : new[] { ContractNames.SyncHandler, ContractNames.SyncVoidHandler })
            .Select(n => assembly?.GetTypeByMetadataName(n)).ToArray();
        var matches = handler.AllInterfaces.Where(i => contracts.Any(c => Same(i.OriginalDefinition, c)) &&
            i.TypeArguments[0] is INamedTypeSymbol request && Same(request.OriginalDefinition, definition.OriginalDefinition)).ToArray();
        if (matches.Length != 1 || !IsOpenDefinition(handler) || HasGenericContainer(handler) || HasGenericContainer(definition) ||
            definition.TypeKind != TypeKind.Class || handler.TypeKind != TypeKind.Class)
        { error = "An open Send route requires one selected family-specific generic class handler contract and a class request without generic containers."; return null; }
        var contract = matches[0];
        var pattern = (INamedTypeSymbol)contract.TypeArguments[0];
        var binding = new CanonicalStreamOpenBinding(compilation, handler, pattern);
        var used = Parameters(pattern).ToArray();
        if (handler.TypeParameters.Any(p => !used.Any(u => Same(p, u)) || p.AllowsRefLikeType) ||
            !binding.Contains(pattern) || !binding.Public(pattern) || !binding.Public(handler))
        { error = "Every Send handler argument must be determined by the public, ownable request pattern."; return null; }
        var response = contract.TypeArguments.Length == 2 ? contract.TypeArguments[1] : null;
        var responseRequest = assembly?.GetTypeByMetadataName(async ? ContractNames.Request : ContractNames.SyncRequest);
        var voidRequest = assembly?.GetTypeByMetadataName(async ? ContractNames.VoidRequest : ContractNames.SyncVoidRequest);
        var responses = pattern.AllInterfaces.Where(i => Same(i.OriginalDefinition, responseRequest)).ToArray();
        var referenceResponse = async && allowReferenceResponse && response is ITypeParameterSymbol parameter &&
            parameter.HasReferenceTypeConstraint && parameter.ReferenceTypeConstraintNullableAnnotation != NullableAnnotation.Annotated &&
            parameter.NullableAnnotation != NullableAnnotation.Annotated && !parameter.AllowsRefLikeType &&
            used.Any(p => Same(p, parameter)) && binding.Contains(parameter) && binding.Public(parameter) &&
            parameter.ConstraintTypes.All(binding.Public) && responses.Length == 1 &&
            SymbolEqualityComparer.IncludeNullability.Equals(responses[0].TypeArguments[0], parameter);
        // IRequest inherits IRequest<Unit>; the selected native void handler still has one type argument.
        var voidResponseMatches = async ? responses.Length == 1 && Same(responses[0].TypeArguments[0], assembly?.GetTypeByMetadataName("Zendiator.Unit")) : responses.Length == 0;
        if (response == null ? !voidResponseMatches || !pattern.AllInterfaces.Any(i => Same(i, voidRequest)) :
            response.SpecialType != SpecialType.System_Int32 && !referenceResponse || responses.Length != 1 || !Same(responses[0].TypeArguments[0], response))
        {
            error = allowReferenceResponse
                ? "The open Send handler requires the matching int, void, or nonnullable request-owned class response contract."
                : "The open Send handler must match the selected family's request int response or void contract.";
            return null;
        }
        error = binding.ApplyConstraints(pattern);
        binding._sendResponse = response;
        return error == null ? binding : null;
    }
    internal bool IsSendResponse(ITypeSymbol type) => _sendResponse != null &&
        SymbolEqualityComparer.IncludeNullability.Equals(type, _sendResponse);
    internal bool IsRequestReferenceResponse(ITypeSymbol type) => _sendResponse is ITypeParameterSymbol && IsSendResponse(type);
    internal bool Contains(ITypeSymbol type) => Parameters(type).All(p => _names.ContainsKey(p));
    internal bool Public(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol p => _names.ContainsKey(p) && !p.AllowsRefLikeType,
        IArrayTypeSymbol a => Public(a.ElementType),
        INamedTypeSymbol n => n.DeclaredAccessibility == Accessibility.Public && !n.IsRefLikeType &&
            (n.ContainingType == null || Public(n.ContainingType)) && n.TypeArguments.All(Public),
        _ => SymbolUtilities.Public(type)
    };
    internal string? ApplyConstraints(INamedTypeSymbol type)
    {
        if (!Contains(type)) return "Stream services contain type parameters outside the request pattern.";
        for (var current = type; current != null; current = current.ContainingType)
        {
            var args = current.TypeArguments.ToArray();
            for (var i = 0; i < current.Arity; i++)
            {
                if (args[i] is ITypeParameterSymbol parameter && Same(parameter.ContainingSymbol, _owner))
                {
                    if (!GenericConstraints.MergeHandlerParam(_constraints[parameter.Ordinal], current.OriginalDefinition.TypeParameters[i],
                        current.OriginalDefinition, args, _compilation))
                        return "Stream request/handler/stage constraints have incompatible reference/value requirements.";
                }
                else if (!GenericConstraints.SatisfiesParameterConstraints(current.OriginalDefinition, args, i, _compilation))
                    return "Constraints on a fixed or constructed Stream argument are not proved by this request pattern. Supply a compatible pattern or closed route.";
            }
        }
        return null;
    }
    internal BoundStreamGenericEmission? Emission(out string? error)
    {
        error = null;
        if (_constraints.Any(m => m.Types.Any(t => !Contains(t))))
        { error = "A Stream constraint contains an unprojected type parameter; its enclosing generic constraint shape is unsupported."; return null; }
        foreach (var value in _constraints)
        {
            var classes = value.Types.OfType<INamedTypeSymbol>().Where(t => t.TypeKind == TypeKind.Class).ToArray();
            if ((value.Struct || value.Unmanaged) && classes.Length != 0)
            { error = "Stream constraints require both a value type and a class base type."; return null; }
            foreach (var left in classes)
                foreach (var right in classes)
                    if (!Same(left, right) && !Converts(left, right) && !Converts(right, left))
                    { error = "Stream constraints require incompatible class base types."; return null; }
            foreach (var wider in classes.Where(w => classes.Any(n => !Same(w, n) && Converts(n, w)))) value.Types.Remove(wider);
            if (classes.Length != 0)
            {
                for (var i = 0; i < value.Types.Count; i++)
                    if (value.Types[i] is INamedTypeSymbol { TypeKind: TypeKind.Class } t && (value.NotNull || value.Class && !value.ClassNullable))
                        value.Types[i] = t.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                value.Class = value.NotNull = false; // A base class is the primary constraint.
            }
            if (value.NotNull && value.Class) value.ClassNullable = false;
        }
        var visiting = new HashSet<int>(); var visited = new HashSet<int>();
        bool Cycle(int i)
        {
            if (visited.Contains(i)) return false;
            if (!visiting.Add(i)) return true;
            foreach (var p in _constraints[i].Types.OfType<ITypeParameterSymbol>().Where(p => Same(p.ContainingSymbol, _owner) && p.Ordinal != i))
                if (Cycle(p.Ordinal)) return true;
            visiting.Remove(i); visited.Add(i); return false;
        }
        if (Enumerable.Range(0, _constraints.Length).Any(Cycle))
        { error = "Stream constraints contain a circular type parameter dependency."; return null; }
        var clauses = _constraints.Select((m, i) =>
        {
            var parts = new List<string>();
            if (m.Class) parts.Add(m.ClassNullable ? "class?" : "class");
            else if (m.Unmanaged) parts.Add("unmanaged");
            else if (m.Struct) parts.Add("struct");
            else if (m.NotNull) parts.Add("notnull");
            parts.AddRange(m.Types.Where(t => t is not ITypeParameterSymbol p || !Same(p.ContainingSymbol, _owner) || p.Ordinal != i)
                .OrderBy(t => t is INamedTypeSymbol { TypeKind: TypeKind.Class } ? 0 : 1).Select(Display));
            if (m.New && !m.Struct && !m.Unmanaged) parts.Add("new()");
            return parts.Count == 0 ? "" : " where " + m.Name + " : " + string.Join(", ", parts);
        });
        return new(new(_constraints.Select(m => m.Name)), new(clauses));
        bool Converts(ITypeSymbol a, ITypeSymbol b) => ((CSharpCompilation)_compilation).ClassifyConversion(a, b).IsImplicit;
    }
    internal static bool MayOverlap(ITypeSymbol left, ITypeSymbol right, Compilation compilation)
    {
        var map = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
        ITypeSymbol Resolve(ITypeSymbol type)
        {
            while (type is ITypeParameterSymbol p && map.TryGetValue(p, out var value)) type = value;
            return type;
        }
        bool Occurs(ITypeParameterSymbol parameter, ITypeSymbol type)
        {
            type = Resolve(type);
            return Same(parameter, type) || Parameters(type).Any(p => !Same(p, type) && Occurs(parameter, p));
        }
        bool Unify(ITypeSymbol a, ITypeSymbol b)
        {
            a = Resolve(a); b = Resolve(b);
            if (Same(a, b)) return true;
            if (a is ITypeParameterSymbol p)
            {
                if (Occurs(p, b)) return false;
                map.Add(p, b); return true;
            }
            if (b is ITypeParameterSymbol q) return Unify(q, a);
            if (a is IArrayTypeSymbol x && b is IArrayTypeSymbol y) return x.Rank == y.Rank && Unify(x.ElementType, y.ElementType);
            if (a is INamedTypeSymbol l && b is INamedTypeSymbol r)
                return Same(l.OriginalDefinition, r.OriginalDefinition) && l.TypeArguments.Length == r.TypeArguments.Length &&
                    (l.ContainingType == null && r.ContainingType == null || l.ContainingType != null && r.ContainingType != null && Unify(l.ContainingType, r.ContainingType)) &&
                    l.TypeArguments.Zip(r.TypeArguments, Unify).All(x => x);
            return RegistrationIdentity(a, compilation) == RegistrationIdentity(b, compilation);
        }
        // Constraint-domain disjointness is not guessed; a structurally possible overlap is rejected.
        return Unify(left, right);
    }
    private static IEnumerable<ITypeParameterSymbol> Parameters(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol p) yield return p;
        else if (type is IArrayTypeSymbol a)
            foreach (var q in Parameters(a.ElementType)) yield return q;
        else if (type is INamedTypeSymbol n)
        {
            if (n.ContainingType != null) foreach (var q in Parameters(n.ContainingType)) yield return q;
            foreach (var argument in n.TypeArguments) foreach (var q in Parameters(argument)) yield return q;
        }
    }
}
