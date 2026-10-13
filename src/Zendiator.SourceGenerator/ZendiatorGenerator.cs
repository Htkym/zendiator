using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Zendiator.SourceGenerator;

/// <summary>Generates concrete request overloads, continuations and DI registration.</summary>
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
        var producerCatalog=markedBindings.Combine(context.CompilationProvider).Select(static(input,ct)=>GeneratedHandlerCatalog.Produce(input.Right,input.Left,ct));
        var producerCatalogSource=producerCatalog.Select(static(result,_)=>result.Source).WithTrackingName("GeneratedHandlerCatalogSource");
        context.RegisterSourceOutput(producerCatalogSource,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource(GeneratedHandlerCatalog.HintName,SourceText.From(text,Encoding.UTF8));});
        context.RegisterSourceOutput(producerCatalog,static(ctx,result)=>{foreach(var diagnostic in result.Diagnostics)ctx.ReportDiagnostic(diagnostic);});
        var markedValue = marked.Select(static (candidate, _) => candidate.Model).WithTrackingName("MarkedHandlerContractEntry").Collect()
            .Select(static (models, _) => new MarkedHandlerCatalog(new(models.GroupBy(m=>m.Identity).Select(g=>g.First()).OrderBy(m=>m.Identity,System.StringComparer.Ordinal))));
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
                input.Left.Right, input.Right, ct).SingleOrDefault()).Where(static binding=>binding!=null).Select(static(binding,_)=>binding!);
        var compositions = compositionBindings.Collect(); // Current symbols and locations only; not value equality.
        var compositionEntries = compositionBindings.Select(static(binding,_)=>binding.Model).WithTrackingName("CompositionContractEntry");
        var compositionValue = compositionEntries.Collect().Select(static (entries, _) => new CompositionSelectionCatalog(new(entries)));
        var compositionModel = compositionValue.Select(static (model, _) => model).WithTrackingName("CompositionSelectionModel");
        var routeEntries = compositionEntries.SelectMany(static(entry,_)=>CanonicalRouteIncremental.Project(entry)).WithTrackingName("CanonicalRouteContractEntry");
        var routeGraphs = routeEntries.Select(static(route,_)=>CanonicalRouteIncremental.Graph(route)).WithTrackingName("CanonicalRouteGraphEntry");
        var routeBackends = routeGraphs.Select(static(route,_)=>CanonicalRouteIncremental.Lower(route)).WithTrackingName("CanonicalRouteBackendEntry");
        var streamConnections = compositionModel.Combine(routeBackends.Collect())
            .Select(static (input, _) => CanonicalStreamEmissionNormalizer.Connect(input.Left, input.Right))
            .WithTrackingName("CanonicalStreamConnectionModel");
        var streamPrototypeModels = streamConnections.SelectMany(static (catalog, _) => catalog.Routes
            .Where(c => c.Kind == CanonicalStreamConnectionKind.Candidate).Select(c => c.Candidate!))
            .WithTrackingName("CanonicalStreamPrototypeProjection");
        var routeTextEntries = routeBackends.Select(static(route,_)=>CanonicalRouteIncremental.Render(route)).WithTrackingName("CanonicalRouteSourceEntry");
        var routeTexts = routeTextEntries.Collect();
        var streamPrototypeFiles = streamPrototypeModels.Collect().Combine(routeTexts)
            .SelectMany(static (input, _) => CanonicalStreamEmissionNormalizer.Files(input.Left, input.Right))
            .WithTrackingName("CanonicalStreamPrototypeSource");
        context.RegisterSourceOutput(streamPrototypeFiles, static (ctx, file) => ctx.AddSource(file.HintName, SourceText.From(file.Source, Encoding.UTF8)));
        var splitRouteEntries = routeTextEntries.Select(static(route,_)=>new CanonicalRouteFile(route,CanonicalSplitSourceEmitter.Route(route))).WithTrackingName("CanonicalRouteFileEntry");
        var executionValue = compositionModel.Combine(routeGraphs.Collect()).Select(static (input, _) => CanonicalRouteIncremental.AssembleGraphs(input.Left,input.Right));
        var executionModel = executionValue.Select(static (model, _) => model).WithTrackingName("CanonicalExecutionGraphModel");
        var canonicalSync = compositionModel.Combine(executionModel)
            .Select(static (input, _) => CanonicalSyncEmissionNormalizer.Create(input.Left, input.Right)).WithTrackingName("CanonicalSyncEmissionModel");
        var canonicalSyncSource = canonicalSync.Combine(routeTexts).Select(static (input, _) => CanonicalRouteIncremental.Sync(input.Left,input.Right)).WithTrackingName("CanonicalSyncSourceEmission");
        var canonicalSyncSourceFiles=canonicalSyncSource.SelectMany(static(text,_)=>CanonicalSplitSourceEmitter.Composition(text)).WithTrackingName("canonicalSyncSourceCompositionFiles");
        context.RegisterSourceOutput(canonicalSyncSourceFiles,static(ctx,file)=>ctx.AddSource(file.HintName,SourceText.From(file.Source,Encoding.UTF8)));
        var canonicalSyncSites = canonicalSync.Combine(compositions).Combine(context.CompilationProvider)
            .Select(static (input, ct) => CanonicalSyncEmissionNormalizer.BindCallSites(input.Left.Left, input.Left.Right, input.Right, ct));
        var canonicalSyncInterceptors = canonicalSyncSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model))
            .WithTrackingName("CanonicalSyncInterceptorsEmission");
        context.RegisterSourceOutput(canonicalSyncInterceptors, static (ctx, text) =>
        {
            if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendSync.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var canonicalAsync = compositionModel.Combine(executionModel)
            .Select(static (input, _) => CanonicalAsyncEmissionNormalizer.Create(input.Left, input.Right)).WithTrackingName("CanonicalAsyncEmissionModel");
        var canonicalAsyncSource = canonicalAsync.Combine(routeTexts).Select(static (input, _) => CanonicalRouteIncremental.Async(input.Left,input.Right)).WithTrackingName("CanonicalAsyncSourceEmission");
        var canonicalAsyncSourceFiles=canonicalAsyncSource.SelectMany(static(text,_)=>CanonicalSplitSourceEmitter.Composition(text)).WithTrackingName("canonicalAsyncSourceCompositionFiles");
        context.RegisterSourceOutput(canonicalAsyncSourceFiles,static(ctx,file)=>ctx.AddSource(file.HintName,SourceText.From(file.Source,Encoding.UTF8)));
        var canonicalAsyncSites = canonicalAsync.Combine(compositions).Combine(context.CompilationProvider)
            .Select(static (input, ct) => CanonicalAsyncEmissionNormalizer.BindCallSites(input.Left.Left, input.Left.Right, input.Right, ct));
        var canonicalAsyncInterceptors = canonicalAsyncSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalAsyncShims"))
            .WithTrackingName("CanonicalAsyncInterceptorsEmission");
        context.RegisterSourceOutput(canonicalAsyncInterceptors, static (ctx, text) =>
        {
            if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendAsync.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var canonicalWrapper=compositionModel.Combine(executionModel).Select(static(input,_)=>CanonicalWrapperEmissionNormalizer.Create(input.Left,input.Right)).WithTrackingName("CanonicalWrapperEmissionModel");
        var wrapperSource=canonicalWrapper.Combine(routeTexts).Select(static(input,_)=>CanonicalRouteIncremental.Wrapper(input.Left,input.Right)).WithTrackingName("CanonicalWrapperSourceEmission");
        var wrapperSourceFiles=wrapperSource.SelectMany(static(text,_)=>CanonicalSplitSourceEmitter.Composition(text)).WithTrackingName("wrapperSourceCompositionFiles");
        context.RegisterSourceOutput(wrapperSourceFiles,static(ctx,file)=>ctx.AddSource(file.HintName,SourceText.From(file.Source,Encoding.UTF8)));
        var wrapperSites=canonicalWrapper.Combine(compositions).Combine(context.CompilationProvider).Select(static(input,ct)=>CanonicalWrapperEmissionNormalizer.BindCallSites(input.Left.Left,input.Left.Right,input.Right,ct));
        var wrapperInterceptors=wrapperSites.Select(static(model,_)=>CanonicalSyncSourceEmitter.EmitInterceptors(model,"CanonicalWrapperShims"));
        context.RegisterSourceOutput(wrapperInterceptors,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource("Zendiator.Canonical.Wrappers.Interceptors.g.cs",SourceText.From(text,Encoding.UTF8));});
        var capabilities=compositionModel.Combine(executionModel).Combine(canonicalSync).Combine(canonicalAsync).Combine(canonicalWrapper)
            .Select(static(input,_)=>CanonicalCapabilityEmissionNormalizer.Create(input.Left.Left.Left.Left,input.Left.Left.Left.Right,input.Left.Left.Right,input.Left.Right,input.Right)).WithTrackingName("CanonicalCapabilityEmissionModel");
        var capabilitySource=capabilities.Combine(routeTexts).Select(static(input,_)=>CanonicalRouteIncremental.Capability(input.Left,input.Right)).WithTrackingName("CanonicalCapabilitySourceEmission");
        var capabilitySourceFiles=capabilitySource.SelectMany(static(text,_)=>CanonicalSplitSourceEmitter.Composition(text)).WithTrackingName("capabilitySourceCompositionFiles");
        context.RegisterSourceOutput(capabilitySourceFiles,static(ctx,file)=>ctx.AddSource(file.HintName,SourceText.From(file.Source,Encoding.UTF8)));
        var capabilitySites=capabilities.Combine(compositions).Combine(context.CompilationProvider).Select(static(input,ct)=>CanonicalCapabilityEmissionNormalizer.BindCallSites(input.Left.Left,input.Left.Right,input.Right,ct));
        var capabilityInterceptors=capabilitySites.Select(static(model,_)=>CanonicalSyncSourceEmitter.EmitInterceptors(model,"CanonicalCapabilityShims"));
        context.RegisterSourceOutput(capabilityInterceptors,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource("Zendiator.Canonical.Capabilities.Interceptors.g.cs",SourceText.From(text,Encoding.UTF8));});
        var multi=compositionModel.Combine(executionModel).Select(static(input,_)=>CanonicalMultiEmissionNormalizer.Create(input.Left,input.Right)).WithTrackingName("CanonicalMultiEmissionModel");
        var multiSource=multi.Combine(routeTexts).Select(static(input,_)=>CanonicalMultiSourceEmitter.Emit(input.Left,r=>CanonicalRouteIncremental.Find(input.Right,r.Registration.Namespace,r.Registration.Handler.RequestName,r.Sync!=null?BoundDispatchFamily.SendSync:r.Async!=null?BoundDispatchFamily.SendAsync:r.Wrapper!=null?r.Wrapper.Family:r.Capability!.Family))).WithTrackingName("CanonicalMultiSourceEmission");
        var multiSourceFiles=multiSource.SelectMany(static(text,_)=>CanonicalSplitSourceEmitter.Composition(text)).WithTrackingName("multiSourceCompositionFiles");
        context.RegisterSourceOutput(multiSourceFiles,static(ctx,file)=>ctx.AddSource(file.HintName,SourceText.From(file.Source,Encoding.UTF8)));
        var acceptedRoutes=canonicalSync.Combine(canonicalAsync).Combine(canonicalWrapper).Combine(capabilities).Combine(multi)
            .Select(static(input,_)=>CanonicalSplitSourceEmitter.Accepted(input.Left.Left.Left.Left,input.Left.Left.Left.Right,input.Left.Left.Right,input.Left.Right,input.Right)).WithTrackingName("CanonicalAcceptedRouteFiles");
        var selectedRouteFiles=splitRouteEntries.Combine(acceptedRoutes)
            .Select(static(input,_)=>CanonicalSplitSourceEmitter.SelectRoute(input.Left.Route,input.Left.File,input.Right))
            .Where(static(file)=>file!=null).Select(static(file,_)=>file!).WithTrackingName("CanonicalRouteOutputFile");
        context.RegisterSourceOutput(selectedRouteFiles,static(ctx,file)=>ctx.AddSource(file.HintName,SourceText.From(file.Source,Encoding.UTF8)));
        var multiSites=multi.Combine(compositions).Combine(context.CompilationProvider).Select(static(input,ct)=>CanonicalMultiEmissionNormalizer.BindCallSites(input.Left.Left,input.Left.Right,input.Right,ct));
        var multiInterceptors=multiSites.Select(static(model,_)=>CanonicalSyncSourceEmitter.EmitInterceptors(model,"CanonicalMultiShims"));
        context.RegisterSourceOutput(multiInterceptors,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource("Zendiator.Canonical.Multiple.Interceptors.g.cs",SourceText.From(text,Encoding.UTF8));});
        context.RegisterSourceOutput(compositionModel.Combine(executionModel).Combine(compositions).Combine(canonicalSync).Combine(canonicalSyncSites)
            .Combine(canonicalAsync).Combine(canonicalAsyncSites).Combine(canonicalWrapper).Combine(wrapperSites).Combine(capabilities).Combine(capabilitySites).Combine(multi).Combine(multiSites).Combine(streamConnections), static (ctx, connected) =>
        {
            var input = connected.Left;
            var originalInput=input.Left.Left;var wrapperInput=originalInput.Left.Left;var original=wrapperInput.Left.Left;var sync = original.Left.Left;
            CompositionSelectionDiscovery.Report(ctx, sync.Left.Left.Left.Left, sync.Left.Left.Right, sync.Left.Left.Left.Right, sync.Left.Right, sync.Right,
                original.Left.Right, original.Right,wrapperInput.Left.Right,wrapperInput.Right,originalInput.Left.Right,originalInput.Right,input.Left.Right,input.Right,connected.Right);
        });
        var legacyInvocations = invocationBindings.Combine(compositions)
            .Select(static (input, _) => CompositionSelectionDiscovery.LegacyInvocations(input.Left, input.Right));
        // Discovery metadata alone never gates unrelated legacy generation.
        var result = declarations.Combine(legacyInvocations).Combine(context.CompilationProvider)
            .Select(static (input, ct) => new GenerationAnalysis(input.Left.Left, input.Left.Right, input.Right, ct).Generate())
            .WithTrackingName("Analysis");

        // Structural equality stops template expansion before allocating generated text.
        var model = result.Select(static (r, _) => r.Model).WithTrackingName("EmissionModel");
        var fusion = model.Select(static (m, _) => m == null ? null : FusionGenerationNormalizer.Create(m))
            .WithTrackingName("FusionNormalization");
        var source = fusion.Select(static (m, _) => m == null ? "" : new SourceEmitter(m).Emit())
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
