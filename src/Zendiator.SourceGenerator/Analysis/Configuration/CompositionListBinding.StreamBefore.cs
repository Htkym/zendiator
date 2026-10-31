using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.MarkedHandlerDiscovery;

namespace Zendiator.SourceGenerator;

internal sealed partial class CompositionListBinding
{
    private sealed record BeforeDeclaration(string Anchor, INamedTypeSymbol Request, string RequestName,
        ImmutableArray<Entry> Entries, int Ordinal);
    private readonly List<BeforeDeclaration> _streamBeforeDeclarations = new();
    private readonly HashSet<string> _matchedStreamBefore = new(StringComparer.Ordinal);

    private void ParseStreamBefore(InvocationExpressionSyntax call, IMethodSymbol method, int ordinal)
    {
        var anchor = "streamBefore:" + ordinal;
        Sources.Add(anchor, call.GetLocation());
        var arguments = call.ArgumentList.Arguments;
        var offset = method.Arity == 0 ? 1 : 0;
        var request = method.Arity == 1 ? method.TypeArguments[0] as INamedTypeSymbol :
            method.Arity == 0 && arguments.Count > 0 && arguments[0].Expression is TypeOfExpressionSyntax typeOf
                ? _semantic.GetTypeInfo(typeOf.Type, _ct).Type as INamedTypeSymbol : null;
        if (request == null)
        {
            Reject("UnsupportedGenerationShape", anchor, "StreamBefore requires a known request type and direct positional literal typeof entries.");
            return;
        }
        var requestName = Name(request.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
        if (request.IsUnboundGenericType)
            request = request.OriginalDefinition;
        if (arguments.Any(a => a.NameColon != null || a.RefKindKeyword != default))
        {
            Reject("UnsupportedGenerationShape", anchor, "StreamBefore requires a known request type and direct positional literal typeof entries.");
            _streamBeforeDeclarations.Add(new(anchor, request, requestName, ImmutableArray<Entry>.Empty, ordinal));
            return;
        }
        var entries = ImmutableArray.CreateBuilder<Entry>();
        for (var index = offset; index < arguments.Count; index++)
        {
            _ct.ThrowIfCancellationRequested();
            var entryAnchor = anchor + "/entry:" + (index - offset);
            Sources.Add(entryAnchor, arguments[index].GetLocation());
            var type = arguments[index].Expression is TypeOfExpressionSyntax beforeType
                ? _semantic.GetTypeInfo(beforeType.Type, _ct).Type as INamedTypeSymbol : null;
            if (type == null)
            {
                Reject("UnsupportedGenerationShape", entryAnchor, "StreamBefore accepts only literal typeof borrowed services; arrays, helpers and session factories are not evaluated.");
                continue;
            }
            var prior = entries.FirstOrDefault(e => Key(e.Type) == Key(type));
            if (prior != null)
                Reject("UnsupportedGenerationShape", entryAnchor, "A StreamBefore service occurs more than once in the ordered list.", prior.Anchor);
            entries.Add(new(type, entryAnchor));
        }
        if (arguments.Count == offset)
            Reject("UnsupportedGenerationShape", anchor, "StreamBefore requires at least one service; omit an empty prelude declaration.");
        foreach (var prior in _streamBeforeDeclarations)
            if (Key(prior.Request) == Key(request) ||
                (IsOpenDefinition(prior.Request) || IsOpenDefinition(request)) &&
                CanonicalStreamOpenBinding.MayOverlap(prior.Request, request, _compilation))
                Reject("AmbiguousPipelineOrder", anchor, "StreamBefore requires one authoritative, nonoverlapping request list; open/closed specialization priority is not inferred.", prior.Anchor);
        _streamBeforeDeclarations.Add(new(anchor, request, requestName, entries.ToImmutable(), ordinal));
    }

    private BeforeDeclaration? FindStreamBefore(INamedTypeSymbol request, BoundDispatchFamily? family,
        out EquatableArray<string> anchors)
    {
        anchors = default;
        if (family != BoundDispatchFamily.Stream || _streamBeforeDeclarations.Count == 0)
            return null;
        var matches = _streamBeforeDeclarations.Where(d => MatchesBeforeRequest(d.Request, request)).ToArray();
        anchors = new(matches.Select(d => d.Anchor));
        foreach (var declaration in matches)
            _matchedStreamBefore.Add(declaration.Anchor);
        return matches.Length == 1 ? matches[0] : null;
    }

    private bool MatchesBeforeRequest(INamedTypeSymbol declaration, INamedTypeSymbol request) =>
        Key(declaration) == Key(request) || IsOpenDefinition(declaration) &&
        CanonicalStreamOpenBinding.MayOverlap(declaration, request, _compilation);

    private (EquatableArray<BoundStreamBeforeEmission> Calls, BoundStreamBeforeRegistration? Registration)
        BindStreamBefore(BeforeDeclaration? declaration, INamedTypeSymbol request, CanonicalStreamOpenBinding? open)
    {
        if (declaration == null || HasDeclarationError(declaration.Anchor))
            return default;
        var calls = new List<BoundStreamBeforeEmission>();
        var services = new List<string>();
        var closed = new Dictionary<string, string>(StringComparer.Ordinal);
        var initialFacts = Facts.Count;
        foreach (var supplied in declaration.Entries)
        {
            _ct.ThrowIfCancellationRequested();
            var closure = CanonicalStreamTypeClosure.Before(supplied.Type, request, _compilation, open);
            if (closure.Type is not { } receiver)
            {
                Reject("UnsupportedGenerationShape", supplied.Anchor, closure.Error!);
                continue;
            }
            if (!Eligible(receiver, supplied.Anchor, "StreamBefore", open: open))
                continue;
            if (closed.TryGetValue(Key(receiver), out var prior))
            {
                Reject("UnsupportedGenerationShape", supplied.Anchor, "StreamBefore entries close to the same service type.", prior);
                continue;
            }
            closed.Add(Key(receiver), supplied.Anchor);
            var call = BindStreamBeforeCallback(receiver, request, supplied.Anchor, open);
            if (call == null)
                continue;
            calls.Add(call);
            services.Add(open == null ? call.ServiceName : DefinitionName(receiver));
        }
        if (Facts.Count != initialFacts || calls.Count != declaration.Entries.Length)
            return default;
        return (new(calls), new(declaration.RequestName,
            new(declaration.Entries.Select(e => Name(e.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))),
            new(services), declaration.Ordinal));
    }

    private BoundStreamBeforeEmission? BindStreamBeforeCallback(INamedTypeSymbol receiver, INamedTypeSymbol request,
        string anchor, CanonicalStreamOpenBinding? open)
    {
        var definition = _compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly
            .GetTypeByMetadataName("Zendiator.IAsyncStreamBefore`1");
        var contracts = receiver.AllInterfaces.Where(i => Same(i.OriginalDefinition, definition)).ToArray();
        var relevant = contracts.Where(i => Key(i.TypeArguments[0]) == Key(request)).ToArray();
        if (relevant.Length != 1)
        {
            Reject(relevant.Length > 1 ? "AmbiguousStageContract" : contracts.Length == 0 ? "StageContractMismatch" : "StageTypeMismatch", anchor,
                "StreamBefore requires exactly one trusted IAsyncStreamBefore<TRequest> contract for this route.");
            return null;
        }
        var contract = relevant[0];
        var members = contract.GetMembers().OfType<IMethodSymbol>().ToArray();
        var member = members.Length == 1 ? members[0] : null;
        var implementation = member == null ? null : receiver.FindImplementationForInterfaceMember(member) as IMethodSymbol;
        var valueTask = _compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        var token = _compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
        if (member == null || implementation == null || implementation.IsAbstract ||
            !Signature(member) || !Signature(implementation))
        {
            Reject("StageContractMismatch", anchor, "StreamBefore requires BeforeAsync(request, CancellationToken) returning non-generic ValueTask without ref, out or scoped parameters.");
            return null;
        }
        string BoundName(ITypeSymbol type) => open?.Display(type) ?? Name(type);
        return new(BoundName(receiver), BoundName(contract), member.Name, receiver.IsValueType)
        {
            CallTarget = CanonicalAsyncCallBinding.Bind(receiver, member, implementation, _compilation, BoundName)
        };

        bool Signature(IMethodSymbol method) => (method.Name == "BeforeAsync" || !method.ExplicitInterfaceImplementations.IsEmpty) &&
            !method.IsStatic && method.Arity == 0 && !method.ReturnsByRef && !method.ReturnsByRefReadonly &&
            Same(method.ReturnType, valueTask) && method.Parameters.Length == 2 &&
            Same(method.Parameters[0].Type, request) && Same(method.Parameters[1].Type, token) &&
            method.Parameters.All(p => p.RefKind == RefKind.None && p.ScopedKind == ScopedKind.None);
    }

    private void ReportUnusedStreamBefore(IEnumerable<(Declaration Declaration, BoundDispatchFamily? Family)> routes)
    {
        foreach (var declaration in _streamBeforeDeclarations.Where(d => !_matchedStreamBefore.Contains(d.Anchor)))
        {
            var wrongFamily = routes.Any(r => MatchesBeforeRequest(declaration.Request, r.Declaration.Request) && r.Family != BoundDispatchFamily.Stream);
            Reject(wrongFamily ? "UnsupportedDispatchFamily" : "UnsupportedGenerationShape", declaration.Anchor,
                wrongFamily ? "StreamBefore can only attach to an explicit Stream route." :
                    "StreamBefore requires an existing explicit Stream Pipeline or Handlers list with a matching request declaration.");
        }
    }
}
