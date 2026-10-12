using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Zendiator.SourceGenerator.SymbolUtilities;
using static Zendiator.SourceGenerator.MarkedHandlerDiscovery;

namespace Zendiator.SourceGenerator;

/// <summary>Literal ordered lists are frontend input. No execution sites are inferred here.</summary>
internal sealed class CompositionListBinding
{
    private sealed record Entry(INamedTypeSymbol Type, string Anchor, FusionStageKind Kind = FusionStageKind.Hook, INamedTypeSymbol? Factory = null);
    private sealed record Declaration(string Anchor, INamedTypeSymbol Request, BoundDispatchFamily? Family,
        bool HandlerList, ImmutableArray<Entry> Entries);
    private readonly Compilation _compilation;
    private readonly SemanticModel _semantic;
    private readonly ImmutableArray<Candidate> _marked;
    private readonly Action<INamedTypeSymbol> _select;
    private readonly CancellationToken _ct;
    private readonly List<Declaration> _declarations = new();
    internal readonly Dictionary<string, Location> Sources = new(StringComparer.Ordinal);
    internal readonly List<CompositionDiagnosticFact> Facts = new();

    internal CompositionListBinding(Compilation compilation, SemanticModel semantic, ImmutableArray<Candidate> marked,
        Action<INamedTypeSymbol> select, CancellationToken ct) =>
        (_compilation, _semantic, _marked, _select, _ct) = (compilation, semantic, marked, select, ct);

    private void Reject(string key, string anchor, string message, string? additional = null) => Facts.Add(new(key, anchor, message, additional));
    private string Name(ITypeSymbol type) => CanonicalSymbolName.Display(type, _compilation);
    private string Key(ITypeSymbol type) => RegistrationIdentity(type, _compilation);

    internal void Parse(InvocationExpressionSyntax call, IMethodSymbol method, int ordinal)
    {
        var anchor = "list:" + ordinal;
        Sources.Add(anchor, call.GetLocation());
        var handlerList = method.Name == "Handlers";
        if (method.Arity != 1 || method.TypeArguments[0] is not INamedTypeSymbol request)
        {
            Reject("UnsupportedGenerationShape", anchor, "The request must be a known closed class or struct.");
            return;
        }
        var arguments = call.ArgumentList.Arguments;
        if (arguments.Any(a => a.NameColon != null || a.RefKindKeyword != default))
        {
            Reject("UnsupportedGenerationShape", anchor, "Ordered lists require direct positional arguments.");
            return;
        }
        BoundDispatchFamily? family = null;
        var offset = handlerList || method.Parameters[0].Type.Name == "DispatchFamily" ? 1 : 0;
        if (offset == 1)
        {
            var definition = _compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly.GetTypeByMetadataName("Zendiator.DispatchFamily");
            var constant = arguments.Count == 0 ? default : _semantic.GetConstantValue(arguments[0].Expression, _ct);
            if (arguments.Count == 0 || !Same(_semantic.GetTypeInfo(arguments[0].Expression, _ct).Type, definition) ||
                !constant.HasValue || constant.Value is not int value || value < 0 || value > (int)BoundDispatchFamily.Notification)
            {
                Reject("UnsupportedDispatchFamily", anchor, "An explicit family requires a known DispatchFamily constant.");
                return;
            }
            family = (BoundDispatchFamily)value;
        }
        var entries = ImmutableArray.CreateBuilder<Entry>();
        var stage = _compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly.GetTypeByMetadataName("Zendiator.Stage");
        var handlerEntry = _compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly.GetTypeByMetadataName("Zendiator.HandlerEntry");
        for (var index = offset; index < arguments.Count; index++)
        {
            _ct.ThrowIfCancellationRequested();
            var entryAnchor = anchor + "/entry:" + (index - offset);
            var expression = arguments[index].Expression;
            Sources.Add(entryAnchor, expression.GetLocation());
            INamedTypeSymbol? type = null;
            INamedTypeSymbol? factoryType = null;
            var kind = FusionStageKind.Hook;
            if (handlerList && expression is TypeOfExpressionSyntax handlerType)
                type = _semantic.GetTypeInfo(handlerType.Type, _ct).Type as INamedTypeSymbol;
            else if (handlerList && expression is InvocationExpressionSyntax handlerFactory &&
                _semantic.GetSymbolInfo(handlerFactory, _ct).Symbol is IMethodSymbol handlerMethod && Same(handlerMethod.ContainingType, handlerEntry) &&
                handlerMethod.Name is "Service" or "Session" && handlerFactory.ArgumentList.Arguments.All(a => a.NameColon == null))
            {
                type = handlerMethod.Arity is 1 or 2 && handlerFactory.ArgumentList.Arguments.Count == 0
                    ? handlerMethod.TypeArguments[0] as INamedTypeSymbol
                    : handlerMethod.Name == "Service" && handlerFactory.ArgumentList.Arguments.Count == 1 &&
                        handlerFactory.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax serviceType
                            ? _semantic.GetTypeInfo(serviceType.Type, _ct).Type as INamedTypeSymbol : null;
                if (handlerMethod.Name == "Session" && handlerMethod.Arity == 2 && handlerFactory.ArgumentList.Arguments.Count == 0)
                {
                    factoryType = handlerMethod.TypeArguments[1] as INamedTypeSymbol;
                    if (factoryType == null)
                    {
                        Reject("UnsupportedGenerationShape", entryAnchor, "A handler session factory requires a known closed type; an unbound type parameter is not a DI service declaration.");
                        continue;
                    }
                }
            }
            else if (!handlerList && expression is InvocationExpressionSyntax factory &&
                _semantic.GetSymbolInfo(factory, _ct).Symbol is IMethodSymbol factoryMethod && Same(factoryMethod.ContainingType, stage) &&
                factoryMethod.Name is "Hook" or "Wrapper" && factory.ArgumentList.Arguments.All(a => a.NameColon == null))
            {
                kind = factoryMethod.Name == "Hook" ? FusionStageKind.Hook : FusionStageKind.Wrapper;
                type = factoryMethod.Arity is 1 or 2 && factory.ArgumentList.Arguments.Count == 0
                    ? factoryMethod.TypeArguments[0] as INamedTypeSymbol
                    : factory.ArgumentList.Arguments.Count == 1 && factory.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax stageType
                        ? _semantic.GetTypeInfo(stageType.Type, _ct).Type as INamedTypeSymbol : null;
                if (factoryMethod.Arity == 2 && factory.ArgumentList.Arguments.Count == 0)
                {
                    factoryType = factoryMethod.TypeArguments[1] as INamedTypeSymbol;
                    if (factoryType == null)
                    {
                        Reject("UnsupportedGenerationShape", entryAnchor, "A session factory requires a known closed type; an unbound type parameter is not a DI service declaration.");
                        continue;
                    }
                }
            }
            if (type == null)
            {
                Reject("UnsupportedGenerationShape", entryAnchor, handlerList
                    ? "Handlers requires literal typeof or HandlerEntry.Service/Session entries; helpers and array variables are not evaluated."
                    : "Pipeline requires literal Stage.Hook/Wrapper entries with a known type; helpers and array variables are not evaluated.");
                continue;
            }
            var prior = entries.FirstOrDefault(e => Key(e.Type) == Key(type));
            if (prior != null) Reject("UnsupportedGenerationShape", entryAnchor,
                $"Type {Name(type)} occurs more than once in this ordered list. Stage tags do not create separate registrations.", prior.Anchor);
            entries.Add(new(type, entryAnchor, kind, factoryType));
            if (handlerList && (_marked.Any(c => Same(c.Symbol, type.OriginalDefinition)) || BindExplicitMetadata(_compilation,type,_ct)!=null)) _select(type);
        }
        _declarations.Add(new(anchor, request, family, handlerList, entries.ToImmutable()));
    }

    internal EquatableArray<BoundPipelineModel> Bind(IEnumerable<INamedTypeSymbol> selected)
    {
        var selectedTypes = selected.ToArray();
        var output = new List<BoundPipelineModel>();
        var resolved = new List<(Declaration Declaration, BoundDispatchFamily? Family)>();
        foreach (var declaration in _declarations)
        {
            var family = declaration.Family;
            if (family == null)
            {
                var explicitFamilies = _declarations.Where(d => d.HandlerList && Key(d.Request) == Key(declaration.Request))
                    .Select(d => d.Family!.Value).Distinct().ToArray();
                var candidates = explicitFamilies.Length != 0 ? explicitFamilies : selectedTypes
                    .SelectMany(t => AvailableFamilies(t, declaration.Request)).Distinct().ToArray();
                if (candidates.Length == 1) family = candidates[0];
                else Reject("UnsupportedDispatchFamily", declaration.Anchor, "Pipeline family is absent or ambiguous; declare the dispatch family explicitly.");
            }
            var prior = resolved.FirstOrDefault(d => d.Declaration.HandlerList == declaration.HandlerList &&
                Key(d.Declaration.Request) == Key(declaration.Request) && d.Family == family);
            if (prior.Declaration != null) Reject("AmbiguousPipelineOrder", declaration.Anchor,
                "Each route requires one authoritative Pipeline list and one authoritative Handlers list.", prior.Declaration.Anchor);
            resolved.Add((declaration, family));
        }
        // Handler-only routes also retain their branch order. Matched lists belong to one route.
        foreach (var (declaration, family) in resolved.Where(d => !d.Declaration.HandlerList ||
            !resolved.Any(p => !p.Declaration.HandlerList && p.Family == d.Family && Key(p.Declaration.Request) == Key(d.Declaration.Request))))
        {
            _ct.ThrowIfCancellationRequested();
            var explicitHandlers = resolved.FirstOrDefault(d => d.Declaration.HandlerList && d.Family == family &&
                Key(d.Declaration.Request) == Key(declaration.Request)).Declaration;
            var before = Facts.Count;
            if (!Eligible(declaration.Request, declaration.Anchor, "Request", concrete: false) || family == null)
            {
                output.Add(Model(null, null, Array.Empty<BoundHandlerModel>(), Array.Empty<BoundStageModel>(), declaration.HandlerList));
                continue;
            }
            if (family == BoundDispatchFamily.Notification)
            {
                Reject("UnsupportedDispatchFamily", declaration.Anchor, "Notification callback metadata and execution are not connected yet.");
                output.Add(Model(null, null, Array.Empty<BoundHandlerModel>(), Array.Empty<BoundStageModel>(), declaration.HandlerList));
                continue;
            }
            var multiple = family is BoundDispatchFamily.SendAllAsync or BoundDispatchFamily.SendAllSync;
            if (multiple && explicitHandlers == null)
            {
                Reject("AmbiguousPipelineOrder", declaration.Anchor,
                    "SendAll requires an explicit Handlers list to declare branch order; candidate identity or discovery order cannot supply it.");
                output.Add(Model(null, null, Array.Empty<BoundHandlerModel>(), Array.Empty<BoundStageModel>(), false));
                continue;
            }
            var handlerEntries = explicitHandlers?.Entries ?? selectedTypes.Where(t => Contracts(t, declaration.Request, family.Value).Any())
                .OrderBy(Identity, StringComparer.Ordinal).Select(t => new Entry(t, declaration.Anchor)).ToImmutableArray();
            var handlers = new List<BoundHandlerModel>();
            ITypeSymbol? result = null;
            FusionResultKind? resultKind = null;
            foreach (var entry in handlerEntries)
            {
                if (!Eligible(entry.Type, entry.Anchor, "Handler")) continue;
                var candidate = _marked.FirstOrDefault(c => Same(c.Symbol, entry.Type.OriginalDefinition)) ?? BindExplicitMetadata(_compilation,entry.Type,_ct);
                if (candidate == null)
                {
                    Reject("UnsupportedGenerationShape", entry.Anchor, $"Handler {Name(entry.Type)} requires a source ZendiatorHandler marker or a supported explicitly supplied metadata type.");
                    continue;
                }
                if (candidate.Model.CompilerOwnsMarkerError) continue;
                var contracts = Contracts(entry.Type, declaration.Request, family.Value).ToArray();
                if (contracts.Length != 1)
                {
                    Reject("UnsupportedGenerationShape", entry.Anchor, $"Handler {Name(entry.Type)} requires exactly one matching request/family/result or void contract.");
                    continue;
                }
                var contract = contracts[0];
                var nextResult = contract.TypeArguments.Length == 2 ? contract.TypeArguments[1] : null;
                if (nextResult != null && (HasOpenShape(nextResult) || nextResult.IsRefLikeType))
                {
                    Reject(nextResult.IsRefLikeType ? "RefLikeCapture" : "UnsupportedGenerationShape", entry.Anchor,
                        "Open or ref-like result/item binding is not connected yet.");
                    continue;
                }
                if (nextResult != null && !Public(nextResult))
                {
                    Reject("UnsupportedGenerationShape", entry.Anchor, "Public route signatures require a public response/item type, including containing types and generic arguments.");
                    continue;
                }
                var nextKind = family == BoundDispatchFamily.Stream ? FusionResultKind.Item : nextResult == null ? FusionResultKind.Void : FusionResultKind.Response;
                if (resultKind != null && (nextKind != resultKind || (nextResult != null && (result == null || Key(nextResult) != Key(result)))))
                    Reject("CapabilityResultShapeMismatch", entry.Anchor, "All handlers in one route must have the same response/void or item shape.");
                resultKind ??= nextKind;
                result ??= nextResult;
                var member = contract.GetMembers().OfType<IMethodSymbol>().SingleOrDefault();
                var implementation = member == null ? null : entry.Type.FindImplementationForInterfaceMember(member) as IMethodSymbol;
                var handlerModel = new BoundHandlerModel(entry.Anchor, Identity(entry.Type), Name(entry.Type), Identity(contract), nextResult == null ? null : Identity(nextResult),
                    implementation == null ? "<missing>" : implementation.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) +
                    "/return=" + Identity(implementation.ReturnType) + "/constraints=" + Constraints(implementation.TypeParameters),
                    FusionOwnership.BorrowedService, implementation == null, implementation == null ? null : new(
                        BoundCallbackPhase.Handler, family == BoundDispatchFamily.Stream ? BoundCompletionKind.Enumerable :
                            family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous,
                        Identity(contract), implementation.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        new(implementation.Parameters.Select(p => new BoundParameterModel(Identity(p.Type), p.RefKind.ToString(), p.ScopedKind.ToString()))),
                        Identity(implementation.ReturnType), Constraints(implementation.TypeParameters),
                        family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync));
                if ((family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync) && declaration.Request.TypeKind is TypeKind.Class or TypeKind.Struct &&
                    entry.Type.TypeKind is (TypeKind.Class or TypeKind.Struct) && _compilation.IsSymbolAccessibleWithin(entry.Type,_compilation.Assembly) && implementation != null && member != null &&
                    (nextResult == null || nextResult is INamedTypeSymbol or IArrayTypeSymbol))
                {
                    if (family == BoundDispatchFamily.SendSync)
                        handlerModel = handlerModel with { SyncEmission = new(Name(declaration.Request), Name(entry.Type),
                            Name(contract), member.Name, nextResult == null ? null : Name(nextResult)){RequestIsValueType=declaration.Request.IsValueType,HandlerIsValueType=entry.Type.IsValueType} };
                    else
                        handlerModel = handlerModel with { AsyncEmission = new(Name(declaration.Request), Name(entry.Type),
                            Name(contract), member.Name, nextResult == null ? null : Name(nextResult)){RequestIsValueType=declaration.Request.IsValueType,HandlerIsValueType=entry.Type.IsValueType} };
                }
                if (entry.Factory != null && !handlerModel.CompilerOwnsError)
                {
                    var binding = SessionFactoryContractBinding.Bind(entry.Factory, entry.Type, declaration.Request, family.Value,
                        entry.Anchor, _compilation, Facts);
                    handlerModel = handlerModel with { CompilerOwnsError = binding.CompilerOwnsError,
                        ServiceOwnership = binding.Session?.Ownership ?? handlerModel.ServiceOwnership,
                        Sessions = binding.Session == null ? default : new(new[] { binding.Session! }) };
                }
                handlers.Add(handlerModel);
            }
            if ((explicitHandlers == null || !HasDeclarationError(explicitHandlers.Anchor)) &&
                ((multiple && handlerEntries.Length == 0) || (!multiple && handlerEntries.Length != 1)))
                Reject("UnsupportedGenerationShape", explicitHandlers?.Anchor ?? declaration.Anchor,
                    multiple ? "SendAll requires at least one handler." : "Send/Stream requires exactly one selected handler; use an explicit list to resolve ambiguity.");
            var stages = new List<BoundStageModel>();
            if (resultKind != null && handlers.Count == handlerEntries.Length)
                foreach (var entry in declaration.HandlerList ? ImmutableArray<Entry>.Empty : declaration.Entries)
                {
                    var stageModel = StageContractBinding.Bind(entry.Type, entry.Kind, entry.Anchor, declaration.Request, family.Value,
                        resultKind.Value, result, _compilation, Facts);
                    if (entry.Factory != null && !stageModel.CompilerOwnsError)
                    {
                        var binding = SessionFactoryContractBinding.Bind(entry.Factory, entry.Type, declaration.Request, family.Value,
                            entry.Anchor, _compilation, Facts);
                        stageModel = stageModel with { CompilerOwnsError = binding.CompilerOwnsError,
                            ServiceOwnership = binding.Session?.Ownership ?? stageModel.ServiceOwnership,
                            Sessions = binding.Session == null ? default : new(new[] { binding.Session! }) };
                    }
                    stages.Add(stageModel);
                }
            output.Add(Model(resultKind, result, handlers, stages, explicitHandlers != null));
            if (Facts.Count == before && !HasDeclarationError(declaration.Anchor) &&
                (explicitHandlers == null || !HasDeclarationError(explicitHandlers.Anchor)) && !stages.Any(s => s.CompilerOwnsError) &&
                !handlers.Any(h => h.CompilerOwnsError) && handlers.Count == handlerEntries.Length && resultKind != null)
                Reject("IncompleteExecutionContract", declaration.Anchor,
                    "Dedicated callback metadata is bound. New canonical emission remains blocked pending its graph and runtime backend acceptance.");

            BoundPipelineModel Model(FusionResultKind? shape, ITypeSymbol? resultType, IEnumerable<BoundHandlerModel> boundHandlers,
                IEnumerable<BoundStageModel> boundStages, bool explicitOrder) => new(declaration.Anchor, Identity(declaration.Request), Name(declaration.Request),
                    family, shape, resultType == null ? null : Identity(resultType), new(boundHandlers), new(boundStages), explicitOrder, FusionContractOrigin.NewCanonical)
                { HandlerListOrdinal = explicitHandlers==null?int.MaxValue:int.Parse(explicitHandlers.Anchor.Substring(5),System.Globalization.CultureInfo.InvariantCulture),
                    PipelineListOrdinal = declaration.HandlerList?int.MaxValue:int.Parse(declaration.Anchor.Substring(5),System.Globalization.CultureInfo.InvariantCulture),
                    HasEmptyPipeline = !declaration.HandlerList && declaration.Entries.Length == 0,
                    PipelineFamily = declaration.HandlerList ? null : declaration.Family };
        }
        return new(output);
    }

    private bool HasDeclarationError(string anchor) => Facts.Any(f => f.Anchor == anchor || f.Anchor.StartsWith(anchor + "/", StringComparison.Ordinal) ||
        f.AdditionalAnchor == anchor || (f.AdditionalAnchor?.StartsWith(anchor + "/", StringComparison.Ordinal) ?? false));

    private bool Eligible(INamedTypeSymbol type, string anchor, string role, bool concrete = true)
    {
        if (type.TypeKind == TypeKind.Error) return false; // Compiler owns unresolved types.
        if (type.IsRefLikeType) { Reject("RefLikeCapture", anchor, $"{role} {Name(type)} cannot be captured in a DI route."); return false; }
        if (type.IsUnboundGenericType || HasOpenShape(type) ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || (concrete && (type.IsAbstract || type.IsStatic)))
        { Reject("UnsupportedGenerationShape", anchor, $"{role} {Name(type)} requires a closed class or struct" +
            (concrete ? " with a concrete implementation." : ".") + " Open binding is pending."); return false; }
        if (!_compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly))
        { Reject("UnsupportedGenerationShape", anchor, $"{role} {Name(type)} is inaccessible to generated code."); return false; }
        if (role == "Request" && !Public(type))
        { Reject("UnsupportedGenerationShape", anchor, "Public route signatures require a public request type, including containing types and generic arguments."); return false; }
        return true;
    }

    private IEnumerable<BoundDispatchFamily> AvailableFamilies(INamedTypeSymbol handler, INamedTypeSymbol request)
    {
        foreach (var family in new[] { BoundDispatchFamily.SendAsync, BoundDispatchFamily.SendSync, BoundDispatchFamily.Stream, BoundDispatchFamily.Notification })
            if (Contracts(handler, request, family).Any()) yield return family;
    }

    private IEnumerable<INamedTypeSymbol> Contracts(INamedTypeSymbol handler, INamedTypeSymbol request, BoundDispatchFamily family)
    {
        var names = family switch
        {
            BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync => new[] { ContractNames.Handler, ContractNames.VoidHandler },
            BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAllSync => new[] { ContractNames.SyncHandler, ContractNames.SyncVoidHandler },
            BoundDispatchFamily.Stream => new[] { ContractNames.StreamHandler },
            _ => new[] { ContractNames.NotificationHandler }
        };
        var assembly = _compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly;
        return handler.AllInterfaces.Where(i => names.Any(n => Same(i.OriginalDefinition, assembly?.GetTypeByMetadataName(n))) &&
            Key(i.TypeArguments[0]) == Key(request));
    }
}
