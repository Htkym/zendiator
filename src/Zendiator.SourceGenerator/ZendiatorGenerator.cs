using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Zendiator.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ZendiatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var marked = context.SyntaxProvider.ForAttributeWithMetadataName(
            MarkedHandlerDiscovery.MarkerName,
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, ct) => MarkedHandlerDiscovery.Bind(ctx, ct))
            .Where(static candidate => candidate != null).Select(static (candidate, _) => candidate!);
        var markedBindings = marked.Collect();
        var producerCatalog = markedBindings.Combine(context.CompilationProvider).Select(static (input, ct) => GeneratedHandlerCatalog.Produce(input.Right, input.Left, ct));
        var producerCatalogSource = producerCatalog.Select(static (result, _) => result.Source).WithTrackingName("GeneratedHandlerCatalogSource");
        context.RegisterSourceOutput(producerCatalogSource, static (ctx, text) => { if (text.Length != 0) ctx.AddSource(GeneratedHandlerCatalog.HintName, SourceText.From(text, Encoding.UTF8)); });
        context.RegisterSourceOutput(producerCatalog, static (ctx, result) => { foreach (var diagnostic in result.Diagnostics) ctx.ReportDiagnostic(diagnostic); });
        var markedValue = marked.Select(static (candidate, _) => candidate.Model).WithTrackingName("MarkedHandlerContractEntry").Collect()
            .Select(static (models, _) => new MarkedHandlerCatalog(new(models.GroupBy(m => m.Identity).Select(g => g.First()).OrderBy(m => m.Identity, System.StringComparer.Ordinal))));
        // Equality keeps the value cached while the current binding stream refreshes Locations.
        var markedModel = markedValue.Select(static (model, _) => model).WithTrackingName("MarkedHandlerModel");
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Zendiator.GenerateZendiatorAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol).Collect();
        var invocations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "AddZendiator" }
            },
            static (ctx, _) => (InvocationExpressionSyntax)ctx.Node);
        var invocationBindings = invocations.Collect();
        var compositionBindings = invocations.Combine(context.CompilationProvider).Combine(markedBindings).Combine(markedModel)
            .Select(static (input, ct) => CompositionSelectionDiscovery.Bind(
                System.Collections.Immutable.ImmutableArray.Create(input.Left.Left.Left), input.Left.Left.Right,
                input.Left.Right, input.Right, ct).SingleOrDefault()).Where(static binding => binding != null).Select(static (binding, _) => binding!);
        var compositions = compositionBindings.Collect(); // Current symbols and locations only; not value equality.
        var registrationSites = compositionBindings.Select(static (binding, _) => binding.RegistrationCallSite)
            .WithTrackingName("CanonicalRegistrationCallSite").Collect()
            .Select(static (sites, _) => new EquatableArray<CanonicalSyncCallSite?>(sites));
        var compositionEntries = compositionBindings.Select(static (binding, _) => binding.Model).WithTrackingName("CompositionContractEntry");
        var compositionValue = compositionEntries.Collect().Select(static (entries, _) => new CompositionSelectionCatalog(new(entries)));
        var compositionModel = compositionValue.Select(static (model, _) => model).WithTrackingName("CompositionSelectionModel");
        var routeEntries = compositionEntries.SelectMany(static (entry, _) => CanonicalRouteIncremental.Project(entry)).WithTrackingName("CanonicalRouteContractEntry");
        var routeGraphs = routeEntries.Select(static (route, _) => CanonicalRouteIncremental.Graph(route)).WithTrackingName("CanonicalRouteGraphEntry");
        var routeBackends = routeGraphs.Select(static (route, _) => CanonicalRouteIncremental.Lower(route)).WithTrackingName("CanonicalRouteBackendEntry");
        var streamConnections = compositionModel.Combine(routeBackends.Collect())
            .Select(static (input, _) => CanonicalStreamEmissionNormalizer.Connect(input.Left, input.Right))
            .WithTrackingName("CanonicalStreamConnectionModel");
        var streamPublic = compositionModel.Combine(streamConnections)
            .Select(static (input, _) => CanonicalStreamPublicConnection.Create(input.Left, input.Right))
            .WithTrackingName("CanonicalStreamPublicModel");
        var streamPublicSites = streamPublic.Combine(registrationSites)
            .Select(static (input, ct) => CanonicalStreamPublicConnection.BindCallSites(input.Left, input.Right, ct));
        var streamPublicFiles = streamPublic.Combine(streamPublicSites)
            .SelectMany(static (input, _) => CanonicalStreamPublicConnection.Files(input.Left, input.Right))
            .Select(static (file, _) => CanonicalRouteServiceEmitter.Unsplit(file));
        context.RegisterSourceOutput(streamPublicFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var streamPublicInterceptors = streamPublicSites.Select(static (model, _) =>
            CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalStreamShims"));
        context.RegisterSourceOutput(streamPublicInterceptors, static (ctx, text) =>
        {
            if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.Stream.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var streamPrototypeModels = streamConnections.SelectMany(static (catalog, _) => catalog.Routes
            .Where(c => c.Kind == CanonicalStreamConnectionKind.Candidate).Select(c => c.Candidate!))
            .WithTrackingName("CanonicalStreamPrototypeProjection");
        var streamCanonicalModels = streamConnections.SelectMany(static (catalog, _) => catalog.Routes
            .Where(c => c.Kind is CanonicalStreamConnectionKind.CanonicalCandidate or CanonicalStreamConnectionKind.HandlerOwned).Select(c => c.Canonical!))
            .WithTrackingName("CanonicalStreamCanonicalProjection");
        var routeTextEntries = routeBackends.Select(static (route, _) => CanonicalRouteIncremental.Render(route)).WithTrackingName("CanonicalRouteSourceEntry");
        var routeTexts = routeTextEntries.Collect();
        var streamPrototypeFiles = streamPrototypeModels.Collect().Combine(streamCanonicalModels.Collect()).Combine(routeTexts)
            .SelectMany(static (input, _) => CanonicalStreamEmissionNormalizer.Files(input.Left.Left, input.Left.Right, input.Right))
            .WithTrackingName("CanonicalStreamPrototypeSource");
        var splitRouteEntries = routeTextEntries.Select(static (route, _) => new CanonicalRouteFile(route, CanonicalSplitSourceEmitter.Route(route))).WithTrackingName("CanonicalRouteFileEntry");
        var routeCompositionFragments = splitRouteEntries.Select(static (entry, _) => entry.File.Composition)
            .Where(static (fragment) => fragment != null).Select(static (fragment, _) => fragment!).Collect();
        var executionValue = compositionModel.Combine(routeGraphs.Collect()).Select(static (input, _) => CanonicalRouteIncremental.AssembleGraphs(input.Left, input.Right));
        var executionModel = executionValue.Select(static (model, _) => model).WithTrackingName("CanonicalExecutionGraphModel");
        var canonicalSync = compositionModel.Combine(executionModel)
            .Select(static (input, _) => CanonicalSyncEmissionNormalizer.Create(input.Left, input.Right)).WithTrackingName("CanonicalSyncEmissionModel");
        var canonicalSyncSourceFiles = canonicalSync.Combine(routeCompositionFragments)
            .SelectMany(static (input, _) => CanonicalSplitSourceEmitter.Composition(input.Left.Compositions.Select(c => c.Namespace), input.Right))
            .WithTrackingName("CanonicalSyncSourceEmission");
        context.RegisterSourceOutput(canonicalSyncSourceFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var canonicalSyncSites = canonicalSync.Combine(registrationSites)
            .Select(static (input, ct) => CanonicalSyncEmissionNormalizer.BindCallSites(input.Left, input.Right, ct));
        var canonicalSyncInterceptors = canonicalSyncSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model))
            .WithTrackingName("CanonicalSyncInterceptorsEmission");
        context.RegisterSourceOutput(canonicalSyncInterceptors, static (ctx, text) =>
        {
            if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendSync.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var allSyncCandidate = compositionModel.Combine(executionModel)
            .Select(static (input, _) => CanonicalSendAllSyncEmissionNormalizer.Create(input.Left, input.Right));
        var allSyncSites = allSyncCandidate.Combine(registrationSites)
            .Select(static (input, ct) => CanonicalSendAllSyncEmissionNormalizer.BindCallSites(input.Left, input.Right, ct));
        var allSync = allSyncCandidate.Combine(allSyncSites).Select(static (input, _) => CanonicalSendAllSyncEmissionNormalizer.Connected(input.Left, input.Right))
            .WithTrackingName("CanonicalSendAllSyncEmissionModel");
        var allSyncFiles = allSync.Combine(routeCompositionFragments)
            .SelectMany(static (input, _) => CanonicalSplitSourceEmitter.Composition(input.Left.Compositions.Select(c => c.Registration.Namespace), input.Right));
        context.RegisterSourceOutput(allSyncFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var allSyncInterceptors = allSyncSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalSendAllSyncShims"));
        context.RegisterSourceOutput(allSyncInterceptors, static (ctx, text) => { if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendAllSync.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8)); });
        var allAsyncCandidate = compositionModel.Combine(executionModel)
            .Select(static (input, _) => CanonicalSendAllAsyncEmissionNormalizer.Create(input.Left, input.Right));
        var allAsyncSites = allAsyncCandidate.Combine(registrationSites)
            .Select(static (input, ct) => CanonicalSendAllAsyncEmissionNormalizer.BindCallSites(input.Left, input.Right, ct));
        var allAsync = allAsyncCandidate.Combine(allAsyncSites).Select(static (input, _) => CanonicalSendAllAsyncEmissionNormalizer.Connected(input.Left, input.Right))
            .WithTrackingName("CanonicalSendAllAsyncEmissionModel");
        var allAsyncFiles = allAsync.Combine(routeCompositionFragments)
            .SelectMany(static (input, _) => CanonicalSplitSourceEmitter.Composition(input.Left.Compositions.Select(c => c.Branch.Namespace), input.Right));
        context.RegisterSourceOutput(allAsyncFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var allAsyncInterceptors = allAsyncSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalSendAllAsyncShims"));
        context.RegisterSourceOutput(allAsyncInterceptors, static (ctx, text) => { if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendAllAsync.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8)); });
        var canonicalAsync = compositionModel.Combine(executionModel)
            .Select(static (input, _) => CanonicalAsyncEmissionNormalizer.Create(input.Left, input.Right)).WithTrackingName("CanonicalAsyncEmissionModel");
        var canonicalAsyncSourceFiles = canonicalAsync.Combine(routeCompositionFragments)
            .SelectMany(static (input, _) => CanonicalSplitSourceEmitter.Composition(input.Left.Compositions.Select(c => c.Namespace), input.Right))
            .WithTrackingName("CanonicalAsyncSourceEmission");
        context.RegisterSourceOutput(canonicalAsyncSourceFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var canonicalAsyncSites = canonicalAsync.Combine(registrationSites)
            .Select(static (input, ct) => CanonicalAsyncEmissionNormalizer.BindCallSites(input.Left, input.Right, ct));
        var canonicalAsyncInterceptors = canonicalAsyncSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalAsyncShims"))
            .WithTrackingName("CanonicalAsyncInterceptorsEmission");
        context.RegisterSourceOutput(canonicalAsyncInterceptors, static (ctx, text) =>
        {
            if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendAsync.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var canonicalWrapper = compositionModel.Combine(executionModel).Select(static (input, _) => CanonicalWrapperEmissionNormalizer.Create(input.Left, input.Right)).WithTrackingName("CanonicalWrapperEmissionModel");
        var wrapperSourceFiles = canonicalWrapper.Combine(routeCompositionFragments)
            .SelectMany(static (input, _) => CanonicalSplitSourceEmitter.Composition(input.Left.Compositions.Select(c => c.Registration.Namespace), input.Right))
            .WithTrackingName("CanonicalWrapperSourceEmission");
        context.RegisterSourceOutput(wrapperSourceFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var wrapperSites = canonicalWrapper.Combine(registrationSites).Select(static (input, ct) => CanonicalWrapperEmissionNormalizer.BindCallSites(input.Left, input.Right, ct));
        var wrapperInterceptors = wrapperSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalWrapperShims"));
        context.RegisterSourceOutput(wrapperInterceptors, static (ctx, text) => { if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.Wrappers.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8)); });
        var capabilities = compositionModel.Combine(executionModel).Combine(canonicalSync).Combine(canonicalAsync).Combine(canonicalWrapper)
            .Select(static (input, _) => CanonicalCapabilityEmissionNormalizer.Create(input.Left.Left.Left.Left, input.Left.Left.Left.Right, input.Left.Left.Right, input.Left.Right, input.Right)).WithTrackingName("CanonicalCapabilityEmissionModel");
        var capabilitySourceFiles = capabilities.Combine(routeCompositionFragments)
            .SelectMany(static (input, _) => CanonicalSplitSourceEmitter.Composition(input.Left.Compositions.Select(c => c.Registration.Namespace), input.Right))
            .WithTrackingName("CanonicalCapabilitySourceEmission");
        context.RegisterSourceOutput(capabilitySourceFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var capabilitySites = capabilities.Combine(registrationSites).Select(static (input, ct) => CanonicalCapabilityEmissionNormalizer.BindCallSites(input.Left, input.Right, ct));
        var capabilityInterceptors = capabilitySites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalCapabilityShims"));
        context.RegisterSourceOutput(capabilityInterceptors, static (ctx, text) => { if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.Capabilities.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8)); });
        var multiCandidate = compositionModel.Combine(executionModel).Combine(streamConnections)
            .Select(static (input, _) => CanonicalMultiEmissionNormalizer.Create(input.Left.Left, input.Left.Right, input.Right));
        var multiSites = multiCandidate.Combine(registrationSites)
            .Select(static (input, ct) => CanonicalMultiEmissionNormalizer.BindCallSites(input.Left, input.Right, ct));
        var multi = multiCandidate.Combine(multiSites)
            .Select(static (input, _) => CanonicalMultiEmissionNormalizer.Connected(input.Left, input.Right)).WithTrackingName("CanonicalMultiEmissionModel");
        var mixedStreamTexts = multi.SelectMany(static (model, _) => CanonicalMultiEmissionNormalizer.StreamRouteTexts(model))
            .WithTrackingName("CanonicalMixedStreamFacadeEntry");
        var mixedStreamFiles = mixedStreamTexts.Select(static (route, _) => CanonicalSplitSourceEmitter.Route(route));
        context.RegisterSourceOutput(mixedStreamFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var streamResolverConnections = streamPublicFiles.Collect().Combine(mixedStreamFiles.Collect()).Combine(streamCanonicalModels.Collect())
            .Select(static (input, _) => CanonicalRouteServiceEmitter.StreamConnections(input.Left.Left.Concat(input.Left.Right), input.Right));
        var resolvedStreamFiles = streamPrototypeFiles.Combine(streamResolverConnections)
            .Select(static (input, _) => CanonicalRouteServiceEmitter.Stream(input.Left, input.Right));
        context.RegisterSourceOutput(resolvedStreamFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var multiCompositionFragments = routeCompositionFragments.Combine(mixedStreamFiles.Select(static (file, _) => file.Composition)
            .Where(static (fragment) => fragment != null).Select(static (fragment, _) => fragment!).Collect())
            .Select(static (input, _) => new EquatableArray<CanonicalCompositionFragment>(input.Left.Concat(input.Right)));
        var multiSourceFiles = multi.Combine(multiCompositionFragments)
            .SelectMany(static (input, _) => CanonicalSplitSourceEmitter.Composition(input.Left, input.Right))
            .WithTrackingName("CanonicalMultiSourceEmission");
        context.RegisterSourceOutput(multiSourceFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var acceptedBaseRoutes = canonicalSync.Combine(canonicalAsync).Combine(canonicalWrapper).Combine(capabilities).Combine(multi)
            .Select(static (input, _) => CanonicalSplitSourceEmitter.Accepted(input.Left.Left.Left.Left, input.Left.Left.Left.Right, input.Left.Left.Right, input.Left.Right, input.Right));
        var acceptedAllSyncRoutes = acceptedBaseRoutes.Combine(allSync).Select(static (input, _) => new EquatableArray<CanonicalOutputSelection>(input.Left.Concat(input.Right.Compositions.Select(c =>
            new CanonicalOutputSelection(c.Registration.Namespace, c.Registration.Handler.RequestName, BoundDispatchFamily.SendAllSync)))));
        var acceptedRoutes = acceptedAllSyncRoutes.Combine(allAsync).Select(static (input, _) => new EquatableArray<CanonicalOutputSelection>(input.Left.Concat(input.Right.Compositions.Select(c =>
            new CanonicalOutputSelection(c.Branch.Namespace, c.Branch.Handler.RequestName, BoundDispatchFamily.SendAllAsync))))).WithTrackingName("CanonicalAcceptedRouteFiles");
        var selectedRouteFiles = splitRouteEntries.Combine(acceptedRoutes)
            .Select(static (input, _) => CanonicalSplitSourceEmitter.SelectRoute(input.Left.Route, input.Left.File, input.Right))
            .Where(static (file) => file != null).Select(static (file, _) => file!).WithTrackingName("CanonicalRouteOutputFile");
        context.RegisterSourceOutput(selectedRouteFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var dispatchCandidates = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => CanonicalMediatorCallSites.Candidate(node), static (ctx, _) => (InvocationExpressionSyntax)ctx.Node).Collect();
        // These three streams contain the complete public API for the reserved canonical Mediator types.
        // Mixed compositions are selected as a whole; legacy generation has a separate API family.
        var dispatchApis = selectedRouteFiles.Select(static (file, _) => CanonicalMediatorCallSites.Api(file)).Collect()
            .Combine(mixedStreamFiles.Select(static (file, _) => CanonicalMediatorCallSites.Api(file)).Collect())
            .Combine(streamPublicFiles.Select(static (file, _) => CanonicalMediatorCallSites.Api(file)).Collect())
            .Select(static (input, _) => new EquatableArray<CanonicalMediatorApi>(input.Left.Left.Concat(input.Left.Right).Concat(input.Right)));
        var dispatchSites = dispatchCandidates.Combine(dispatchApis).Combine(context.CompilationProvider)
            .Select(static (input, ct) => CanonicalMediatorCallSites.Bind(input.Left.Left, input.Left.Right, input.Right, ct))
            .WithTrackingName("CanonicalMediatorCallSites");
        var dispatchFiles = dispatchSites.SelectMany(static (sites, _) => sites)
            .Select(static (site, _) => CanonicalMediatorCallSites.Emit(site)).WithTrackingName("CanonicalMediatorDispatchFile");
        context.RegisterSourceOutput(dispatchFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var multiInterceptors = multiSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalMultiShims"));
        context.RegisterSourceOutput(multiInterceptors, static (ctx, text) => { if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.Multiple.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8)); });
        context.RegisterSourceOutput(compositionModel.Combine(executionModel).Combine(compositions).Combine(canonicalSync).Combine(canonicalSyncSites)
            .Combine(canonicalAsync).Combine(canonicalAsyncSites).Combine(canonicalWrapper).Combine(wrapperSites).Combine(capabilities).Combine(capabilitySites).Combine(multi).Combine(multiSites).Combine(streamConnections).Combine(streamPublic).Combine(streamPublicSites).Combine(allSync).Combine(allSyncSites).Combine(allAsync).Combine(allAsyncSites), static (ctx, extended) =>
        {
            var syncExtended = extended.Left.Left;
            var connected = syncExtended.Left.Left;
            var input = connected.Left.Left.Left;
            var originalInput = input.Left.Left; var wrapperInput = originalInput.Left.Left; var original = wrapperInput.Left.Left; var sync = original.Left.Left;
            CompositionSelectionDiscovery.Report(ctx, sync.Left.Left.Left.Left, sync.Left.Left.Right, sync.Left.Left.Left.Right, sync.Left.Right, sync.Right,
                original.Left.Right, original.Right, wrapperInput.Left.Right, wrapperInput.Right, originalInput.Left.Right, originalInput.Right, input.Left.Right, input.Right, connected.Left.Left.Right, connected.Left.Right, connected.Right, syncExtended.Left.Right, syncExtended.Right, extended.Left.Right, extended.Right);
        });
        var legacyInvocations = invocationBindings.Combine(compositions)
            .Select(static (input, _) => CompositionSelectionDiscovery.LegacyInvocations(input.Left, input.Right));
        // Discovery metadata alone never gates unrelated legacy generation.
        var result = declarations.Combine(legacyInvocations).Combine(context.CompilationProvider)
            .Select(static (input, ct) => new GenerationAnalysis(input.Left.Left, input.Left.Right, input.Right, ct).Generate())
            .WithTrackingName("Analysis");

        // Structural equality stops template expansion before allocating generated text.
        var model = result.Select(static (r, _) => r.Model).WithTrackingName("EmissionModel");
        var source = model.Select(static (m, _) => m == null ? "" : new SourceEmitter(m).Emit())
            .WithTrackingName("SourceEmission")
            .Select(static (text, _) => text).WithTrackingName("SourceText");
        context.RegisterSourceOutput(source, static (ctx, text) =>
        {
            if (text.Length != 0)
                ctx.AddSource("Zendiator.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var target = result.Select(static (r, _) => r.Target).WithTrackingName("InterceptorModel");
        var interceptors = target.Select(static (t, _) => t == null ? "" : SourceEmitter.EmitInterceptors(t))
            .WithTrackingName("InterceptorsEmission")
            .Select(static (text, _) => text).WithTrackingName("InterceptorsText");
        context.RegisterSourceOutput(interceptors, static (ctx, text) =>
        {
            if (text.Length != 0)
                ctx.AddSource("Zendiator.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        context.RegisterSourceOutput(result, static (ctx, r) =>
        {
            foreach (var diagnostic in r.Diagnostics)
                ctx.ReportDiagnostic(diagnostic);
        });
    }
}
