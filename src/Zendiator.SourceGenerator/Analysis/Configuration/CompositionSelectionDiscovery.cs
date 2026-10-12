using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Zendiator.SourceGenerator.SymbolUtilities;

namespace Zendiator.SourceGenerator;

/// <summary>Binds explicit composition membership before either generation backend is chosen.</summary>
internal static class CompositionSelectionDiscovery
{
    internal sealed record Binding(CompositionSelectionModel Model, InvocationExpressionSyntax Invocation,
        ImmutableDictionary<string, Location> Sources);

    internal static ImmutableArray<Binding> Bind(ImmutableArray<InvocationExpressionSyntax> invocations,
        Compilation compilation, ImmutableArray<MarkedHandlerDiscovery.Candidate> marked, MarkedHandlerCatalog catalog, CancellationToken ct)
    {
        var configuration = compilation.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorConfiguration");
        var entryType = configuration?.ContainingAssembly.GetTypeByMetadataName("Zendiator.DependencyInjection.ZendiatorServiceCollectionExtensions");
        var compositionAttribute = compilation.GetTypeByMetadataName(ContractNames.Handler)?.ContainingAssembly
            .GetTypeByMetadataName("Zendiator.ZendiatorCompositionAttribute");
        var result = ImmutableArray.CreateBuilder<Binding>();
        foreach (var invocation in invocations)
        {
            ct.ThrowIfCancellationRequested();
            var semantic = compilation.GetSemanticModel(invocation.SyntaxTree);
            if (semantic.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method ||
                !Same(method.ContainingType, entryType)) continue;
            var lambda = invocation.ArgumentList.Arguments.Select(a => a.Expression).OfType<LambdaExpressionSyntax>().SingleOrDefault();
            // During migration, old nongeneric configuration remains the legacy adapter.
            // An explicit new selection operation identifies a new default composition.
            var explicitDefault = method.Arity == 0 && lambda != null && lambda.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                semantic.GetSymbolInfo(call, ct).Symbol is IMethodSymbol operation && Same(operation.ContainingType, configuration) &&
                operation.Name is "UseLocalHandlers" or "AddHandler" or "Pipeline" or "Handlers");
            if (method.Arity == 0 && !explicitDefault) continue;

            var errors = new List<string>();
            var key = "default";
            var identity = "default";
            string? markerName = null;
            string? generatedNamespace = "Zendiator.Generated.Default";
            var useLocalHandlers = false;
            var hasExplicitSelection = false;
            var compilerOwned = false;
            if (method.Arity == 1 && method.TypeArguments[0] is INamedTypeSymbol composition)
            {
                identity = MarkedHandlerDiscovery.Identity(composition);
                markerName = Name(composition);
                var ns = composition.ContainingNamespace.ToDisplayString(new SymbolDisplayFormat(
                    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers));
                var identifier = SyntaxFacts.GetKeywordKind(composition.Name) != SyntaxKind.None ? "@" + composition.Name : composition.Name;
                generatedNamespace = composition.ContainingType != null ? null :
                    (composition.ContainingNamespace.IsGlobalNamespace ? "Zendiator" : ns) + ".Generated." + identifier;
                var attributes = composition.GetAttributes().Where(a => Same(a.AttributeClass, compositionAttribute)).ToArray();
                compilerOwned = attributes.Length > 1;
                if (!compilerOwned && (attributes.Length != 1 || composition.TypeKind != TypeKind.Class ||
                    !composition.IsSealed || composition.IsStatic || HasParameters(composition) || !Public(composition)))
                    errors.Add("A composition marker must be a public sealed nongeneric class with one ZendiatorComposition attribute.");
                if (attributes.Length == 1)
                {
                    var arguments = attributes[0].ConstructorArguments;
                    if (arguments.Length != 1 || arguments[0].Value is not string value || value.Length == 0 ||
                        value.Trim() != value || value.Any(char.IsControl) || value == "default")
                        errors.Add("The composition key must be nonempty, trimmed, free of control characters and different from default.");
                    else key = value;
                }
            }
            else if (method.Arity != 0) errors.Add("The composition marker could not be bound.");

            var selected = new Dictionary<INamedTypeSymbol, SelectedHandlerModel>(SymbolEqualityComparer.Default);
            var explicitTypes = new HashSet<string>(StringComparer.Ordinal);
            var lists = new CompositionListBinding(compilation, semantic, marked, Select, ct);
            if (explicitDefault) SelectLocal(); // The new default composition has implicit local membership.
            var parameter = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Parameter,
                ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Count == 1 =>
                    parenthesized.ParameterList.Parameters[0],
                _ => null
            };
            var parameterSymbol = parameter == null ? null : semantic.GetDeclaredSymbol(parameter, ct);
            var calls = lambda?.Body switch
            {
                InvocationExpressionSyntax call => new[] { call },
                BlockSyntax block when block.Statements.All(s => s is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax }) =>
                    block.Statements.Cast<ExpressionStatementSyntax>().Select(s => (InvocationExpressionSyntax)s.Expression).ToArray(),
                _ => null
            };
            if (calls == null || parameterSymbol == null || lambda!.AsyncKeyword != default)
                errors.Add("Composition selection requires a direct configuration lambda with supported selection calls; helpers and control flow are not evaluated.");
            else for (var ordinal = 0; ordinal < calls.Length; ordinal++)
            {
                var call = calls[ordinal];
                ct.ThrowIfCancellationRequested();
                if (call.Expression is not MemberAccessExpressionSyntax member ||
                    !Same(semantic.GetSymbolInfo(member.Expression, ct).Symbol, parameterSymbol) ||
                    semantic.GetSymbolInfo(call, ct).Symbol is not IMethodSymbol operation || !Same(operation.ContainingType, configuration))
                {
                    errors.Add("Composition selection must call supported methods directly on the configuration parameter.");
                    continue;
                }
                if (operation.Name == "UseLocalHandlers" && call.ArgumentList.Arguments.Count == 0) { useLocalHandlers = true; SelectLocal(); }
                else if (operation.Name == "AddHandler")
                {
                    hasExplicitSelection = true;
                    var type = operation.Arity == 1 ? operation.TypeArguments[0] as INamedTypeSymbol :
                        call.ArgumentList.Arguments.Count == 1 && call.ArgumentList.Arguments[0].Expression is TypeOfExpressionSyntax typeOf
                            ? semantic.GetTypeInfo(typeOf.Type, ct).Type as INamedTypeSymbol : null;
                    if (type == null) errors.Add("AddHandler requires a known handler type or a typeof expression.");
                    else
                    {
                        if (type.IsUnboundGenericType) type = type.OriginalDefinition;
                        if (!explicitTypes.Add(MarkedHandlerDiscovery.RegistrationIdentity(type, compilation)))
                            errors.Add($"Handler {Name(type)} is explicitly selected more than once.");
                        Select(type);
                    }
                }
                else if (operation.Name is "Pipeline" or "Handlers") lists.Parse(call, operation, ordinal);
                else errors.Add($"Composition selection operation {operation.Name} is not connected yet.");
            }
            var pipelines = lists.Bind(selected.Keys);
            if (generatedNamespace != null && GeneratedTypeConflict(compilation, generatedNamespace)) generatedNamespace = null;
            var model = new CompositionSelectionModel(identity, key,
                new(selected.Values.OrderBy(h => h.Identity, StringComparer.Ordinal)), new(errors), compilerOwned,
                pipelines, new(lists.Facts)) { GeneratedNamespace = generatedNamespace, MarkerName = markerName,
                    MicrosoftDiProviderName = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceProvider") is { TypeKind: TypeKind.Class } provider &&
                        provider.ContainingAssembly.Identity.Name == "Microsoft.Extensions.DependencyInjection" && provider.Locations.All(l => !l.IsInSource) ? Name(provider) : null,
                    UseLocalHandlers = useLocalHandlers, HasExplicitSelection = hasExplicitSelection };
            result.Add(new(model, invocation, lists.Sources.ToImmutableDictionary(StringComparer.Ordinal)));

            void SelectLocal()
            {
                foreach (var candidate in marked) Select(candidate.Symbol);
            }
            void Select(INamedTypeSymbol type)
            {
                var candidate = marked.FirstOrDefault(c => Same(c.Symbol, type.OriginalDefinition));
                if (candidate == null)
                {
                    errors.Add($"Handler {Name(type)} requires a source ZendiatorHandler marker. External catalog binding is not connected yet.");
                    return;
                }
                var definition = catalog.Handlers.First(h => h.Identity == candidate.Model.Identity);
                if (definition.CompilerOwnsMarkerError) { compilerOwned = true; return; }
                selected[type] = new(MarkedHandlerDiscovery.Identity(type), definition);
            }
        }
        return result.ToImmutable();
    }

    internal static CompositionSelectionCatalog Normalize(ImmutableArray<Binding> bindings) => new(new(bindings.Select(b => b.Model)));

    private static bool GeneratedTypeConflict(Compilation compilation, string generatedNamespace)
    {
        var parts = generatedNamespace.Replace("@", "").Split('.');
        // A type at any namespace component, including the full namespace, conflicts with its declaration.
        for (var length = 1; length <= parts.Length; length++)
            if (compilation.GetTypeByMetadataName(string.Join(".", parts.Take(length))) != null) return true;
        return new[] { "Zendiator", "IZendiator", "ZendiatorGeneratedRegistrar" }.Any(name =>
            compilation.GetTypeByMetadataName(string.Join(".", parts) + "." + name) != null);
    }

    internal static ImmutableArray<InvocationExpressionSyntax> LegacyInvocations(ImmutableArray<InvocationExpressionSyntax> invocations,
        ImmutableArray<Binding> compositions) => invocations.Where(i => !compositions.Any(c => c.Invocation == i)).ToImmutableArray();

    internal static void Report(SourceProductionContext context, CompositionSelectionCatalog catalog, ImmutableArray<Binding> current,
        CanonicalExecutionGraphCatalog execution, CanonicalSyncEmissionModel canonicalSync, CanonicalSyncInterceptorModel syncCallSites)
    {
        for (var index = 0; index < catalog.Compositions.Count; index++)
        {
            var composition = catalog.Compositions[index];
            var location = current[index].Invocation.GetLocation();
            var prior = Enumerable.Range(0, index).Where(i => catalog.Compositions[i].Identity == composition.Identity ||
                catalog.Compositions[i].Key == composition.Key).DefaultIfEmpty(-1).First();
            if (prior >= 0)
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.UnsupportedGenerationShape, location,
                    new[] { current[prior].Invocation.GetLocation() }, null,
                    $"Composition {composition.Key} requires one authoritative configuration call site and a unique marker/key."));
            else
            {
                foreach (var error in composition.Errors)
                    context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.UnsupportedGenerationShape, location, error));
                foreach (var fact in composition.Facts)
                {
                    var sources = current[index].Sources;
                    var primary = sources.TryGetValue(fact.Anchor, out var source) ? source : location;
                    var additional = fact.AdditionalAnchor != null && sources.TryGetValue(fact.AdditionalAnchor, out var original)
                        ? new[] { original } : System.Array.Empty<Location>();
                    var message = fact.Message;
                    if (fact.Key == "IncompleteExecutionContract")
                    {
                        var enabled = canonicalSync.Compositions.FirstOrDefault(c => c.Index == index);
                        if (enabled != null && enabled.Hook == null && syncCallSites.CallSites.Any(s => s.Namespace == enabled.Namespace && s.MarkerName == enabled.MarkerName))
                            continue; // Only the runtime matrix accepted by review and its actual registration call site are enabled.
                        var graph = execution.Compositions[index].FirstOrDefault(g => g.Contract.Anchor == fact.Anchor);
                        if (graph != null)
                            message = graph.IsValid
                                ? "The " + (graph.Contract.Family == BoundDispatchFamily.Stream ? "Stream" : "Send") + " execution graph satisfies its structural contract. Canonical runtime generation and equivalence are pending; emission remains blocked."
                                : "The execution graph is incomplete: " + string.Join(", ", graph.Facts.Select(f => f.Reason)) + ". Canonical emission remains blocked.";
                        if (enabled?.Hook != null)
                            message = "The single synchronous Hook backend is a validation candidate only. Production use remains blocked pending its runtime equivalence acceptance.";
                    }
                    context.ReportDiagnostic(Diagnostic.Create(Descriptor(fact.Key), primary, additional, null, message));
                }
                if (composition.Errors.Count == 0 && composition.Facts.Count == 0 && composition.Pipelines.Count == 0 &&
                    (!composition.CompilerOwnsHandlerError || composition.Handlers.Count != 0))
                    context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.UnsupportedGenerationShape, location,
                        $"Composition {composition.Key} requires the 0.5 canonical generation backend, which is not connected yet."));
            }
        }
    }

    private static DiagnosticDescriptor Descriptor(string key) => key switch
    {
        "StageContractMismatch" => GeneratorDiagnostics.StageContractMismatch,
        "StageTypeMismatch" => GeneratorDiagnostics.StageTypeMismatch,
        "AmbiguousStageContract" => GeneratorDiagnostics.AmbiguousStageContract,
        "AmbiguousPipelineOrder" => GeneratorDiagnostics.AmbiguousPipelineOrder,
        "UnsupportedDispatchFamily" => GeneratorDiagnostics.UnsupportedDispatchFamily,
        "AsyncHookOnSyncRoute" => GeneratorDiagnostics.AsyncHookOnSyncRoute,
        "CapabilityResultShapeMismatch" => GeneratorDiagnostics.CapabilityResultShapeMismatch,
        "InaccessibleStage" => GeneratorDiagnostics.InaccessibleStage,
        "RefLikeCapture" => GeneratorDiagnostics.RefLikeCapture,
        "IncompleteExecutionContract" => GeneratorDiagnostics.IncompleteExecutionContract,
        _ => GeneratorDiagnostics.UnsupportedGenerationShape
    };
}
