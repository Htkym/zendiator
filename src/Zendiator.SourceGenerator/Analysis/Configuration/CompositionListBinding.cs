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
        bool HandlerList, ImmutableArray<Entry> Entries, ITypeSymbol? StreamFinal = null);
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
        var arguments = call.ArgumentList.Arguments;
        var requestOffset = method.Arity == 0 ? 1 : 0;
        var typedStream = method.Name == "StreamPipeline";
        ITypeSymbol? streamFinal = typedStream ? method.Arity == 2 ? method.TypeArguments[1] :
            arguments.Count >= 2 && arguments[1].Expression is TypeOfExpressionSyntax finalType ? _semantic.GetTypeInfo(finalType.Type, _ct).Type : null : null;
        var request = method.Arity is 1 or 2 ? method.TypeArguments[0] as INamedTypeSymbol :
            method.Arity == 0 && arguments.Count > 0 && arguments[0].Expression is TypeOfExpressionSyntax requestType
                ? _semantic.GetTypeInfo(requestType.Type, _ct).Type as INamedTypeSymbol : null;
        if (request == null)
        {
            Reject("UnsupportedGenerationShape", anchor, "The request must be a known type argument or literal typeof request definition.");
            return;
        }
        if (request.IsUnboundGenericType) request = request.OriginalDefinition;
        if (typedStream && streamFinal == null)
        { Reject("UnsupportedGenerationShape", anchor, "StreamPipeline requires an explicit final type argument or literal typeof final pattern."); return; }
        if (arguments.Any(a => a.NameColon != null || a.RefKindKeyword != default))
        {
            Reject("UnsupportedGenerationShape", anchor, "Ordered lists require direct positional arguments.");
            return;
        }
        BoundDispatchFamily? family = typedStream ? BoundDispatchFamily.Stream : null;
        var hasFamily = !typedStream && (requestOffset != 0 || handlerList || method.Parameters[0].Type.Name == "DispatchFamily");
        var offset = typedStream && method.Arity == 0 ? 2 : requestOffset + (hasFamily ? 1 : 0);
        if (hasFamily)
        {
            var definition = _compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly.GetTypeByMetadataName("Zendiator.DispatchFamily");
            var constant = arguments.Count <= requestOffset ? default : _semantic.GetConstantValue(arguments[requestOffset].Expression, _ct);
            if (arguments.Count <= requestOffset || !Same(_semantic.GetTypeInfo(arguments[requestOffset].Expression, _ct).Type, definition) ||
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
                    : handlerFactory.ArgumentList.Arguments.Count == (handlerMethod.Name == "Session" ? 2 : 1) &&
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
                else if (handlerMethod.Name == "Session" && handlerMethod.Arity == 0 && handlerFactory.ArgumentList.Arguments.Count == 2 &&
                    handlerFactory.ArgumentList.Arguments[1].Expression is TypeOfExpressionSyntax literalFactory)
                    factoryType = _semantic.GetTypeInfo(literalFactory.Type, _ct).Type as INamedTypeSymbol;
                if (handlerMethod.Name == "Session" && factoryType == null)
                { Reject("UnsupportedGenerationShape", entryAnchor, "A handler factory requires one known type argument or literal typeof definition."); continue; }
            }
            else if (!handlerList && expression is InvocationExpressionSyntax factory &&
                _semantic.GetSymbolInfo(factory, _ct).Symbol is IMethodSymbol factoryMethod && Same(factoryMethod.ContainingType, stage) &&
                factoryMethod.Name is "Hook" or "Wrapper" && factory.ArgumentList.Arguments.All(a => a.NameColon == null))
            {
                kind = factoryMethod.Name == "Hook" ? FusionStageKind.Hook : FusionStageKind.Wrapper;
                type = factoryMethod.Arity is 1 or 2 && factory.ArgumentList.Arguments.Count == 0
                    ? factoryMethod.TypeArguments[0] as INamedTypeSymbol
                    : factory.ArgumentList.Arguments.Count is 1 or 2 && factory.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax stageType
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
                else if (factoryMethod.Arity == 0 && factory.ArgumentList.Arguments.Count == 2 &&
                    factory.ArgumentList.Arguments[1].Expression is TypeOfExpressionSyntax literalFactory)
                    factoryType = _semantic.GetTypeInfo(literalFactory.Type, _ct).Type as INamedTypeSymbol;
                if (factory.ArgumentList.Arguments.Count == 2 && factoryType == null)
                { Reject("UnsupportedGenerationShape", entryAnchor, "A stage factory requires a literal typeof definition."); continue; }
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
            if (handlerList && (_marked.Any(c => Same(c.Symbol, type.OriginalDefinition)) || BindExplicitMetadata(_compilation, type, _ct) != null)) _select(type);
        }
        _declarations.Add(new(anchor, request, family, handlerList, entries.ToImmutable(), streamFinal));
    }

    internal EquatableArray<BoundPipelineModel> Bind(IEnumerable<INamedTypeSymbol> selected)
    {
        var selectedTypes = selected.ToArray();
        var output = new List<BoundPipelineModel>();
        var patterns = new List<(INamedTypeSymbol Request, BoundDispatchFamily Family, bool Open, string Anchor)>();
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
            var request = declaration.Request;
            CanonicalStreamOpenBinding? open = null;
            string BoundName(ITypeSymbol type) => open?.Display(type) ?? Name(type);
            if (family == null)
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
            var asyncSend = family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync;
            var handlerEntries = explicitHandlers?.Entries ?? selectedTypes.Where(t => Contracts(t, declaration.Request, family.Value).Any() ||
                family == BoundDispatchFamily.Stream && CanonicalStreamTypeClosure.Handler(t, declaration.Request, _compilation).Type is { } closed &&
                    Contracts(closed, declaration.Request, family.Value).Any())
                .OrderBy(Identity, StringComparer.Ordinal).Select(t => new Entry(t, declaration.Anchor)).ToImmutableArray();
            if (IsOpenDefinition(request))
            {
                string? error = null;
                if (family == BoundDispatchFamily.Stream && handlerEntries.Length == 1)
                    open = CanonicalStreamOpenBinding.Create(request, handlerEntries[0].Type, _compilation, out error);
                else if (family is (BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync) && handlerEntries.Length == 1)
                    open = CanonicalStreamOpenBinding.CreateSend(request, handlerEntries[0].Type, _compilation, asyncSend, out error,
                        allowReferenceResponse: family == BoundDispatchFamily.SendAsync && handlerEntries[0].Factory == null &&
                            (declaration.HandlerList || declaration.Entries.Length == 0));
                else if (family is (BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync) && handlerEntries.Length == 2 && handlerEntries.All(e => IsOpenDefinition(e.Type)))
                    open = CanonicalStreamOpenBinding.CreateSend(request, handlerEntries[0].Type, _compilation, asyncSend, out error);
                if (open == null)
                {
                    Reject("UnsupportedGenerationShape", declaration.Anchor, error ?? "An open request requires one supported handler-derived Stream/Send pattern or two ordered open SendAll handlers.");
                    output.Add(Model(null, null, Array.Empty<BoundHandlerModel>(), Array.Empty<BoundStageModel>(), explicitHandlers != null));
                    continue;
                }
                request = open.Request;
            }
            var openSend = open != null && family is (BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAllAsync);
            if (!Eligible(request, declaration.Anchor, "Request", concrete: false, open: open, scopedRequest: family == BoundDispatchFamily.SendSync))
            {
                output.Add(Model(null, null, Array.Empty<BoundHandlerModel>(), Array.Empty<BoundStageModel>(), explicitHandlers != null));
                continue;
            }
            foreach (var prior in patterns.Where(p => p.Family == family && (p.Open || open != null)))
                if (CanonicalStreamOpenBinding.MayOverlap(prior.Request, request, _compilation))
                    Reject("AmbiguousPipelineOrder", declaration.Anchor, "Explicit request patterns may overlap. Keep one authoritative open or closed registration; specialization priority is not inferred.", prior.Anchor);
            patterns.Add((request, family.Value, open != null, declaration.Anchor));
            var handlers = new List<BoundHandlerModel>();
            var closedHandlers = new Dictionary<string, string>(StringComparer.Ordinal);
            ITypeSymbol? result = null;
            FusionResultKind? resultKind = null;
            foreach (var supplied in handlerEntries)
            {
                var entry = supplied;
                if (openSend)
                {
                    if (entry.Factory != null)
                    { Reject("UnsupportedGenerationShape", entry.Anchor, "Open Send handler sessions require a separate ownership proof."); continue; }
                    var closure = CanonicalStreamTypeClosure.SendHandler(entry.Type, request, _compilation, open!, asyncSend);
                    if (closure.Type == null)
                    { Reject("UnsupportedGenerationShape", entry.Anchor, closure.Error!); continue; }
                    entry = entry with { Type = closure.Type };
                }
                if (family == BoundDispatchFamily.Stream)
                {
                    var closure = CanonicalStreamTypeClosure.Handler(entry.Type, request, _compilation, open);
                    if (closure.Type == null)
                    { Reject("UnsupportedGenerationShape", entry.Anchor, closure.Error!); continue; }
                    entry = entry with { Type = closure.Type };
                }
                if (!Eligible(entry.Type, entry.Anchor, "Handler", open: open)) continue;
                var candidate = _marked.FirstOrDefault(c => Same(c.Symbol, entry.Type.OriginalDefinition)) ?? BindExplicitMetadata(_compilation, entry.Type, _ct);
                if (candidate == null)
                {
                    Reject("UnsupportedGenerationShape", entry.Anchor, $"Handler {Name(entry.Type)} requires a source ZendiatorHandler marker or a supported explicitly supplied metadata type.");
                    if (Sources.TryGetValue(entry.Anchor, out var source) && source.SourceTree != null &&
                        Zendiator.Tooling.MissingHandlerAttribute.Find(_compilation, source.SourceTree, source.SourceSpan, _ct) is { } fix)
                    {
                        var targetAnchor = entry.Anchor + "/missing-marker";
                        Sources[targetAnchor] = fix.Declaration.Identifier.GetLocation();
                        Facts[Facts.Count - 1] = Facts[Facts.Count - 1] with
                        {
                            AdditionalAnchor = targetAnchor,
                            FixHandler = fix.Properties[Zendiator.Tooling.MissingHandlerAttribute.HandlerKey],
                            FixSourceChecksum = fix.Properties[Zendiator.Tooling.MissingHandlerAttribute.SourceChecksumKey],
                            FixTargetChecksum = fix.Properties[Zendiator.Tooling.MissingHandlerAttribute.TargetChecksumKey]
                        };
                    }
                    continue;
                }
                if (candidate.Model.CompilerOwnsMarkerError) continue;
                if (openSend) _select(entry.Type);
                if (family == BoundDispatchFamily.Stream)
                {
                    var closedKey = Key(entry.Type);
                    if (closedHandlers.TryGetValue(closedKey, out var original))
                    {
                        if (!Facts.Any(f => f.Key == "UnsupportedGenerationShape" && f.Anchor == entry.Anchor && f.AdditionalAnchor == original))
                            Reject("UnsupportedGenerationShape", entry.Anchor, "Stream service entries close to the same handler type.", original);
                        continue;
                    }
                    closedHandlers.Add(closedKey, entry.Anchor);
                    _select(entry.Type); // Exact closed service membership, only after marker validation.
                }
                var contracts = Contracts(entry.Type, request, family.Value).ToArray();
                if (contracts.Length != 1)
                {
                    Reject("UnsupportedGenerationShape", entry.Anchor, $"Handler {Name(entry.Type)} requires exactly one matching request/family/result or void contract.");
                    continue;
                }
                var contract = contracts[0];
                var nextResult = contract.TypeArguments.Length == 2 ? contract.TypeArguments[1] : null;
                if (nextResult != null && (HasOpenShape(nextResult) && (open == null || !open.Contains(nextResult)) || nextResult.IsRefLikeType))
                {
                    Reject(nextResult.IsRefLikeType ? "RefLikeCapture" : "UnsupportedGenerationShape", entry.Anchor,
                        "Open or ref-like result/item binding is not connected yet.");
                    continue;
                }
                if (nextResult != null && !(open?.Public(nextResult) ?? Public(nextResult)))
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
                var requestReferenceResponse = nextResult is ITypeParameterSymbol && open?.IsRequestReferenceResponse(nextResult) == true;
                var handlerModel = new BoundHandlerModel(entry.Anchor, Identity(entry.Type), BoundName(entry.Type), Identity(contract), nextResult == null ? null : Identity(nextResult),
                    implementation == null ? "<missing>" : implementation.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) +
                    "/return=" + Identity(implementation.ReturnType) + "/constraints=" + Constraints(implementation.TypeParameters),
                    FusionOwnership.BorrowedService, implementation == null, implementation == null ? null : new(
                        BoundCallbackPhase.Handler, family == BoundDispatchFamily.Stream ? BoundCompletionKind.Enumerable :
                            family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync ? BoundCompletionKind.ValueTask : BoundCompletionKind.Synchronous,
                        Identity(contract), implementation.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        new(implementation.Parameters.Select(p => new BoundParameterModel(Identity(p.Type), p.RefKind.ToString(), p.ScopedKind.ToString()))),
                        Identity(implementation.ReturnType), Constraints(implementation.TypeParameters),
                        family is BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync))
                {
                    ReceiverIsGeneric = entry.Type.IsGenericType,
                    ReceiverIsPublic = open?.Public(entry.Type) ?? Public(entry.Type),
                    RequestReferenceResponse = requestReferenceResponse,
                    ClosedReferenceResponse = open == null && family == BoundDispatchFamily.SendAsync &&
                        entry.Type.TypeKind == TypeKind.Class && entry.Factory == null &&
                        (declaration.HandlerList || declaration.Entries.Length == 0) && HasClosedReferenceResponse(request, nextResult)
                };
                if ((family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAllSync or BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllAsync) && declaration.Request.TypeKind is TypeKind.Class or TypeKind.Struct &&
                    entry.Type.TypeKind is (TypeKind.Class or TypeKind.Struct) && _compilation.IsSymbolAccessibleWithin(entry.Type, _compilation.Assembly) && implementation != null && member != null &&
                    (nextResult == null || nextResult is INamedTypeSymbol or IArrayTypeSymbol || requestReferenceResponse))
                {
                    if (family is BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAllSync)
                        handlerModel = handlerModel with
                        {
                            SyncEmission = new(BoundName(request), BoundName(entry.Type),
                            BoundName(contract), member.Name, nextResult == null ? null : BoundName(nextResult))
                            { RequestIsValueType = request.IsValueType, RequestIsRefLike = request.IsRefLikeType, HandlerIsValueType = entry.Type.IsValueType }
                        };
                    else
                        handlerModel = handlerModel with
                        {
                            AsyncEmission = new(BoundName(request), BoundName(entry.Type),
                            BoundName(contract), member.Name, nextResult == null ? null : BoundName(nextResult))
                            {
                                RequestIsValueType = request.IsValueType,
                                HandlerIsValueType = entry.Type.IsValueType,
                                CallTarget = family == BoundDispatchFamily.SendAsync
                                    ? CanonicalAsyncCallBinding.Bind(entry.Type, member, implementation, _compilation, BoundName) : null
                            }
                        };
                }
                if (family == BoundDispatchFamily.Stream && implementation != null && member != null && nextResult != null &&
                    declaration.Request.TypeKind is (TypeKind.Class or TypeKind.Struct) &&
                    entry.Type.TypeKind is (TypeKind.Class or TypeKind.Struct) && _compilation.IsSymbolAccessibleWithin(entry.Type, _compilation.Assembly))
                    handlerModel = handlerModel with
                    {
                        StreamEmission = new(BoundName(request), BoundName(nextResult), BoundName(entry.Type),
                        BoundName(contract), member.Name, request.IsValueType, entry.Type.IsValueType)
                    };
                if (entry.Factory != null && !handlerModel.CompilerOwnsError)
                {
                    if (family == BoundDispatchFamily.Stream)
                    {
                        var closure = CanonicalStreamTypeClosure.Factory(entry.Factory, request, entry.Type, _compilation, open);
                        if (closure.Type == null) { Reject("UnsupportedGenerationShape", entry.Anchor, closure.Error!); continue; }
                        entry = entry with { Factory = closure.Type };
                    }
                    var binding = SessionFactoryContractBinding.Bind(entry.Factory, entry.Type, request, family.Value,
                        entry.Anchor, _compilation, Facts, open);
                    handlerModel = handlerModel with
                    {
                        CompilerOwnsError = binding.CompilerOwnsError,
                        ServiceOwnership = binding.Session?.Ownership ?? handlerModel.ServiceOwnership,
                        Sessions = binding.Session == null ? default : new(new[] { binding.Session! })
                    };
                }
                handlers.Add(handlerModel);
            }
            if ((explicitHandlers == null || !HasDeclarationError(explicitHandlers.Anchor)) &&
                ((multiple && handlerEntries.Length == 0) || (!multiple && handlerEntries.Length != 1)))
                Reject("UnsupportedGenerationShape", explicitHandlers?.Anchor ?? declaration.Anchor,
                    multiple ? "SendAll requires at least one handler." : "Send/Stream requires exactly one selected handler; use an explicit list to resolve ambiguity.");
            var stages = new List<BoundStageModel>();
            var registrationStages = new List<RegistrationStageEmission>();
            var sourceItem = result;
            var closedStages = new Dictionary<string, string>(StringComparer.Ordinal);
            if (resultKind != null && handlers.Count == handlerEntries.Length)
                foreach (var supplied in declaration.HandlerList ? ImmutableArray<Entry>.Empty : declaration.Entries)
                {
                    var entry = supplied;
                    if (openSend)
                    {
                        var sendWrapper = entry.Kind == FusionStageKind.Wrapper &&
                            family is (BoundDispatchFamily.SendSync or BoundDispatchFamily.SendAsync or BoundDispatchFamily.SendAllSync) &&
                            resultKind is (FusionResultKind.Response or FusionResultKind.Void);
                        if (entry.Kind != FusionStageKind.Hook && !sendWrapper || entry.Factory != null)
                        { Reject("UnsupportedGenerationShape", entry.Anchor, "Open Send supports borrowed full hooks and supported SendSync/SendAsync/SendAllSync wrappers; sessions require separate proofs."); continue; }
                        var closure = sendWrapper
                            ? CanonicalStreamTypeClosure.SendWrapper(entry.Type, request, result, _compilation, open!, asyncSend)
                            : CanonicalStreamTypeClosure.SendHook(entry.Type, request, _compilation, open!, asyncSend);
                        if (closure.Type == null)
                        { Reject("UnsupportedGenerationShape", entry.Anchor, closure.Error!); continue; }
                        entry = entry with { Type = closure.Type };
                    }
                    if (family == BoundDispatchFamily.Stream && result != null)
                    {
                        var closure = CanonicalStreamTypeClosure.Stage(entry.Type, request, result, entry.Kind, _compilation, open);
                        if (closure.Type == null)
                        { Reject("UnsupportedGenerationShape", entry.Anchor, closure.Error!); continue; }
                        entry = entry with { Type = closure.Type };
                        var closedKey = Key(entry.Type);
                        if (closedStages.TryGetValue(closedKey, out var original))
                        {
                            if (!Facts.Any(f => f.Key == "UnsupportedGenerationShape" && f.Anchor == entry.Anchor && f.AdditionalAnchor == original))
                                Reject("UnsupportedGenerationShape", entry.Anchor, "Stream service entries close to the same stage type.", original);
                            continue;
                        }
                        closedStages.Add(closedKey, entry.Anchor);
                    }
                    var stageModel = StageContractBinding.Bind(entry.Type, entry.Kind, entry.Anchor, request, family.Value,
                        resultKind.Value, result, _compilation, Facts, open, declaration.StreamFinal != null);
                    if (entry.Factory != null && !stageModel.CompilerOwnsError)
                    {
                        if (family == BoundDispatchFamily.Stream)
                        {
                            var closure = CanonicalStreamTypeClosure.Factory(entry.Factory, request, entry.Type, _compilation, open);
                            if (closure.Type == null) { Reject("UnsupportedGenerationShape", entry.Anchor, closure.Error!); continue; }
                            entry = entry with { Factory = closure.Type };
                        }
                        var binding = SessionFactoryContractBinding.Bind(entry.Factory, entry.Type, request, family.Value,
                            entry.Anchor, _compilation, Facts, open);
                        stageModel = stageModel with
                        {
                            CompilerOwnsError = binding.CompilerOwnsError,
                            ServiceOwnership = binding.Session?.Ownership ?? stageModel.ServiceOwnership,
                            Sessions = binding.Session == null ? default : new(new[] { binding.Session! })
                        };
                    }
                    stages.Add(stageModel);
                    if (open != null)
                        registrationStages.Add(new(DefinitionName(supplied.Type), supplied.Kind.ToString(),
                            supplied.Factory == null ? null : DefinitionName(supplied.Factory)));
                    if (declaration.StreamFinal != null && stageModel.StreamItems != null)
                        result = StageContractBinding.StreamOutput(entry.Type, stageModel) ?? result;
                }
            BoundStreamItemFlow? itemFlow = null;
            if (declaration.StreamFinal is { } finalPattern)
            {
                var finalItem = result == null ? null : CanonicalStreamTypeClosure.Final(finalPattern, result, _compilation, open);
                if (finalItem == null || result == null || !SymbolEqualityComparer.IncludeNullability.Equals(result, finalItem))
                    Reject("StageTypeMismatch", declaration.Anchor, "The last Stream stage output does not match the explicitly declared final item type.");
                else if (finalItem.IsRefLikeType || HasOpenShape(finalItem) && (open == null || !open.Contains(finalItem)) || !(open?.Public(finalItem) ?? Public(finalItem)))
                    Reject("UnsupportedGenerationShape", declaration.Anchor, "The final Stream pattern requires public, ownable and fully determined arguments.");
                if (sourceItem != null && result != null && finalItem != null && stages.All(s => s.StreamItems != null))
                    itemFlow = new(new(Identity(sourceItem), BoundName(sourceItem), sourceItem.IsValueType),
                        new(Identity(finalItem), BoundName(finalItem), finalItem.IsValueType), new(stages.Select(s => s.StreamItems!)));
            }
            string? constraintError = null;
            var generic = open?.Emission(out constraintError);
            if (open != null && generic == null)
                Reject("UnsupportedGenerationShape", declaration.Anchor, constraintError!);
            var openHandlerOnly = handlers.Count == 1 && stages.Count == 0 && handlers[0].Sessions.Count == 0 &&
                handlers[0].ServiceOwnership == FusionOwnership.BorrowedService &&
                (family == BoundDispatchFamily.SendSync && handlers[0].SyncEmission is { HandlerIsValueType: false, RequestIsValueType: false } ||
                family == BoundDispatchFamily.SendAsync && handlers[0].AsyncEmission is { HandlerIsValueType: false, RequestIsValueType: false });
            var openSyncWrapperHookRoute = resultKind is (FusionResultKind.Response or FusionResultKind.Void) && handlers.Count == 1 &&
                handlers[0].SyncEmission is { HandlerIsValueType: false, RequestIsValueType: false } && stages.Count == 2 &&
                stages[0].Kind == FusionStageKind.Wrapper && stages[0].WrapperEmission is { WrapperIsValueType: false } &&
                stages[1].Kind == FusionStageKind.Hook && stages[1].SyncEmission is { HookIsValueType: false };
            if (open != null && family == BoundDispatchFamily.SendSync &&
                (handlers.Count != 1 || handlers[0].SyncEmission == null ||
                !openHandlerOnly && !openSyncWrapperHookRoute && (stages.Count != 1 || stages[0].SyncEmission is not { HookIsValueType: false }) ||
                !declaration.HandlerList && declaration.Entries.Length != stages.Count))
                Reject("UnsupportedGenerationShape", declaration.Anchor, "Open SendSync requires one class handler and borrowed class stages: no stages, one full synchronous hook, or one response/native void wrapper followed by one full synchronous hook.");
            var openWrapperRoute = resultKind == FusionResultKind.Response && stages.Count == 1 &&
                stages[0].Kind == FusionStageKind.Wrapper && stages[0].WrapperEmission is { WrapperIsValueType: false };
            var openWrapperHookRoute = resultKind is (FusionResultKind.Response or FusionResultKind.Void) && stages.Count == 2 &&
                stages[0].Kind == FusionStageKind.Wrapper && stages[0].WrapperEmission is { WrapperIsValueType: false } &&
                stages[1].Kind == FusionStageKind.Hook && stages[1].AsyncEmission is { HookIsValueType: false };
            if (open != null && family == BoundDispatchFamily.SendAsync &&
                (handlers.Count != 1 || handlers[0].AsyncEmission == null ||
                !openHandlerOnly && !openWrapperRoute && !openWrapperHookRoute && (stages.Count != 1 && !(resultKind == FusionResultKind.Response && stages.Count == 2) ||
                stages.Any(s => s.AsyncEmission is not { HookIsValueType: false })) ||
                !declaration.HandlerList && declaration.Entries.Length != stages.Count))
                Reject("UnsupportedGenerationShape", declaration.Anchor, "Open SendAsync requires one class handler and borrowed class stages: no stages, one full asynchronous hook, two response hooks, one response wrapper, or one response/native void wrapper followed by one full asynchronous hook.");
            var openAllSync = open != null && family == BoundDispatchFamily.SendAllSync;
            var openAllSyncWrapperHook = resultKind is (FusionResultKind.Response or FusionResultKind.Void) && stages.Count == 2 &&
                stages[0] is { Kind: FusionStageKind.Wrapper, Sessions.Count: 0, ServiceOwnership: FusionOwnership.BorrowedService, WrapperEmission: { WrapperIsValueType: false } } &&
                stages[1] is { Kind: FusionStageKind.Hook, Sessions.Count: 0, ServiceOwnership: FusionOwnership.BorrowedService, SyncEmission: { HookIsValueType: false } };
            if (openAllSync && (handlers.Count != 2 || handlers.Any(h => h.SyncEmission is not { HandlerIsValueType: false, ResultName: null or "int" or "global::System.Int32" }) ||
                resultKind is not (FusionResultKind.Response or FusionResultKind.Void) ||
                !openAllSyncWrapperHook && (stages.Count != 1 || stages[0].SyncEmission is not { HookIsValueType: false }) ||
                !declaration.HandlerList && declaration.Entries.Length != stages.Count))
                Reject("UnsupportedGenerationShape", declaration.Anchor, "Open SendAllSync requires two ordered class handlers with int response or native void and borrowed class stages: one full synchronous hook, or one wrapper followed by one full synchronous hook.");
            var openAllAsync = open != null && family == BoundDispatchFamily.SendAllAsync;
            if (openAllAsync && (handlers.Count != 2 || handlers.Any(h => h.AsyncEmission is not { HandlerIsValueType: false, ResultName: null or "int" or "global::System.Int32" }) ||
                resultKind is not (FusionResultKind.Response or FusionResultKind.Void) || stages.Count != 1 || stages[0].AsyncEmission is not { HookIsValueType: false } ||
                !declaration.HandlerList && declaration.Entries.Length != 1))
                Reject("UnsupportedGenerationShape", declaration.Anchor, "Open SendAllAsync requires two ordered class handlers, int response or native void and one borrowed class full asynchronous hook.");
            var openAll = openAllSync || openAllAsync;
            var openRegistration = open != null && generic != null && handlers.Count == handlerEntries.Length &&
                (handlers.Count == 1 || openAll && handlers.Count == 2)
                ? new BoundStreamOpenRegistration(DefinitionName(declaration.Request), DefinitionName(handlerEntries[0].Type),
                    new(handlerEntries.SelectMany((entry, i) => new[] { Identity(entry.Type), handlers[i].Type })), new(registrationStages))
                {
                    FinalDefinitionName = declaration.StreamFinal == null ? null :
                        Name(declaration.StreamFinal.WithNullableAnnotation(NullableAnnotation.NotAnnotated)),
                    HandlerFactoryDefinitionName = handlerEntries[0].Factory == null ? null : DefinitionName(handlerEntries[0].Factory!),
                    HandlerDefinitionNames = openAll ? new(handlerEntries.Select(entry => DefinitionName(entry.Type))) : default
                } : null;
            output.Add(Model(resultKind, result, handlers, stages, explicitHandlers != null) with
            {
                StreamGeneric = family == BoundDispatchFamily.Stream ? generic : null,
                SendGeneric = openSend ? generic : null,
                SendOpenRegistration = openSend ? openRegistration : null,
                StreamItemFlow = itemFlow,
                StreamDeclaredStages = declaration.StreamFinal == null ? default : new(declaration.Entries.Select(entry =>
                    new RegistrationStageEmission(Name(entry.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)), entry.Kind.ToString(),
                        entry.Factory == null ? null : Name(entry.Factory.WithNullableAnnotation(NullableAnnotation.NotAnnotated))))),
                StreamOpenRegistration = family == BoundDispatchFamily.Stream ? openRegistration : null
            });
            if (Facts.Count == before && !HasDeclarationError(declaration.Anchor) &&
                (explicitHandlers == null || !HasDeclarationError(explicitHandlers.Anchor)) && !stages.Any(s => s.CompilerOwnsError) &&
                !handlers.Any(h => h.CompilerOwnsError) && handlers.Count == handlerEntries.Length && resultKind != null)
                Reject("IncompleteExecutionContract", declaration.Anchor,
                    "Dedicated callback metadata is bound. New canonical emission remains blocked pending its graph and runtime backend acceptance.");

            BoundPipelineModel Model(FusionResultKind? shape, ITypeSymbol? resultType, IEnumerable<BoundHandlerModel> boundHandlers,
                IEnumerable<BoundStageModel> boundStages, bool explicitOrder) => new(declaration.Anchor, Identity(request), BoundName(request),
                    family, shape, resultType == null ? null : Identity(resultType), new(boundHandlers), new(boundStages), explicitOrder, FusionContractOrigin.NewCanonical)
                {
                    HandlerListOrdinal = explicitHandlers == null ? int.MaxValue : int.Parse(explicitHandlers.Anchor.Substring(5), System.Globalization.CultureInfo.InvariantCulture),
                    PipelineListOrdinal = declaration.HandlerList ? int.MaxValue : int.Parse(declaration.Anchor.Substring(5), System.Globalization.CultureInfo.InvariantCulture),
                    HasEmptyPipeline = !declaration.HandlerList && declaration.Entries.Length == 0,
                    PipelineFamily = declaration.HandlerList ? null : declaration.Family,
                    RequestShape = open != null && family == BoundDispatchFamily.SendSync ? BoundRequestShape.OpenSyncReference :
                        open != null && family == BoundDispatchFamily.SendAsync ? BoundRequestShape.OpenAsyncReference :
                        open != null && family == BoundDispatchFamily.SendAllSync ? BoundRequestShape.OpenAllSyncReference :
                        open != null && family == BoundDispatchFamily.SendAllAsync ? BoundRequestShape.OpenAllAsyncReference :
                        request.IsRefLikeType && !request.IsGenericType && !HasOpenShape(request) ? BoundRequestShape.ClosedRefLike :
                        request.TypeKind == TypeKind.Class && !HasOpenShape(request) ? BoundRequestShape.ClosedReference :
                        request.TypeKind == TypeKind.Struct && !request.IsRefLikeType && !request.IsGenericType && !HasOpenShape(request) ?
                            BoundRequestShape.ClosedNonGenericValue : BoundRequestShape.Unsupported,
                    RequestRuntimeIdentity = request.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    RequestIsGeneric = request.IsGenericType
                };
        }
        return new(output);
    }

    private bool HasDeclarationError(string anchor) => Facts.Any(f => f.Anchor == anchor || f.Anchor.StartsWith(anchor + "/", StringComparison.Ordinal) ||
        f.AdditionalAnchor == anchor || (f.AdditionalAnchor?.StartsWith(anchor + "/", StringComparison.Ordinal) ?? false));

    private string DefinitionName(INamedTypeSymbol type) => Name(type.Arity == 0 ? type : type.OriginalDefinition.ConstructUnboundGenericType());

    private bool Eligible(INamedTypeSymbol type, string anchor, string role, bool concrete = true, CanonicalStreamOpenBinding? open = null, bool scopedRequest = false)
    {
        if (type.TypeKind == TypeKind.Error) return false; // Compiler owns unresolved types.
        if (type.IsRefLikeType && !scopedRequest) { Reject("RefLikeCapture", anchor, $"{role} {Name(type)} cannot be captured in a DI route."); return false; }
        if (type.IsUnboundGenericType || HasOpenShape(type) && (open == null || !open.Contains(type)) ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || (concrete && (type.IsAbstract || type.IsStatic)))
        {
            Reject("UnsupportedGenerationShape", anchor, $"{role} {Name(type)} requires a closed class or struct" +
            (concrete ? " with a concrete implementation." : ".") + " Open binding is pending."); return false;
        }
        if (!_compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly))
        { Reject("UnsupportedGenerationShape", anchor, $"{role} {Name(type)} is inaccessible to generated code."); return false; }
        if (role == "Request" && !(open?.Public(type) ?? Public(type)))
        { Reject("UnsupportedGenerationShape", anchor, "Public route signatures require a public request type, including containing types and generic arguments."); return false; }
        return true;
    }

    private bool HasClosedReferenceResponse(INamedTypeSymbol request, ITypeSymbol? result)
    {
        if (request.TypeKind != TypeKind.Class || request.IsGenericType ||
            result is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsGenericType: false, NullableAnnotation: NullableAnnotation.NotAnnotated } ||
            HasOpenShape(result) || !Public(result)) return false;
        var contract = _compilation.GetTypeByMetadataName(ContractNames.Request);
        var responses = request.AllInterfaces.Where(i => Same(i.OriginalDefinition, contract)).ToArray();
        return responses.Length == 1 && SymbolEqualityComparer.IncludeNullability.Equals(responses[0].TypeArguments[0], result);
    }

    private IEnumerable<BoundDispatchFamily> AvailableFamilies(INamedTypeSymbol handler, INamedTypeSymbol request)
    {
        foreach (var family in new[] { BoundDispatchFamily.SendAsync, BoundDispatchFamily.SendSync, BoundDispatchFamily.Stream, BoundDispatchFamily.Notification })
            if (Contracts(handler, request, family).Any()) yield return family;
            else if (family == BoundDispatchFamily.Stream && CanonicalStreamTypeClosure.Handler(handler, request, _compilation).Type is { } closed &&
                Contracts(closed, request, family).Any()) yield return family;
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
            (Key(i.TypeArguments[0]) == Key(request) || family == BoundDispatchFamily.Stream && IsOpenDefinition(request) &&
                i.TypeArguments[0] is INamedTypeSymbol pattern && Same(pattern.OriginalDefinition, request.OriginalDefinition)));
    }
}
