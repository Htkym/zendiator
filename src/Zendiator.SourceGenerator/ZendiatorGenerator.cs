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
            .Where(static candidate => candidate != null).Select(static (candidate, _) => candidate!).Collect();
        var producerCatalog=marked.Combine(context.CompilationProvider).Select(static(input,ct)=>GeneratedHandlerCatalog.Produce(input.Right,input.Left,ct));
        var producerCatalogSource=producerCatalog.Select(static(result,_)=>result.Source).WithTrackingName("GeneratedHandlerCatalogSource");
        context.RegisterSourceOutput(producerCatalogSource,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource(GeneratedHandlerCatalog.HintName,SourceText.From(text,Encoding.UTF8));});
        context.RegisterSourceOutput(producerCatalog,static(ctx,result)=>{foreach(var diagnostic in result.Diagnostics)ctx.ReportDiagnostic(diagnostic);});
        var markedValue = marked.Select(static (candidates, _) => MarkedHandlerDiscovery.Normalize(candidates));
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
            static (ctx, _) => (InvocationExpressionSyntax)ctx.Node).Collect();
        var compositions = invocations.Combine(context.CompilationProvider).Combine(marked).Combine(markedModel)
            .Select(static (input, ct) => CompositionSelectionDiscovery.Bind(input.Left.Left.Left, input.Left.Left.Right,
                input.Left.Right, input.Right, ct));
        var compositionValue = compositions.Select(static (current, _) => CompositionSelectionDiscovery.Normalize(current));
        var compositionModel = compositionValue.Select(static (model, _) => model).WithTrackingName("CompositionSelectionModel");
        var executionValue = compositionModel.Select(static (catalog, _) => CanonicalExecutionGraphNormalizer.Create(catalog));
        var executionModel = executionValue.Select(static (model, _) => model).WithTrackingName("CanonicalExecutionGraphModel");
        var canonicalSync = compositionModel.Combine(executionModel)
            .Select(static (input, _) => CanonicalSyncEmissionNormalizer.Create(input.Left, input.Right)).WithTrackingName("CanonicalSyncEmissionModel");
        var canonicalSyncSource = canonicalSync.Select(static (model, _) => CanonicalSyncSourceEmitter.Emit(model)).WithTrackingName("CanonicalSyncSourceEmission");
        context.RegisterSourceOutput(canonicalSyncSource, static (ctx, text) =>
        {
            if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendSync.g.cs", SourceText.From(text, Encoding.UTF8));
        });
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
        var canonicalAsyncSource = canonicalAsync.Select(static (model, _) => CanonicalAsyncSourceEmitter.Emit(model)).WithTrackingName("CanonicalAsyncSourceEmission");
        context.RegisterSourceOutput(canonicalAsyncSource, static (ctx, text) =>
        {
            if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendAsync.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var canonicalAsyncSites = canonicalAsync.Combine(compositions).Combine(context.CompilationProvider)
            .Select(static (input, ct) => CanonicalAsyncEmissionNormalizer.BindCallSites(input.Left.Left, input.Left.Right, input.Right, ct));
        var canonicalAsyncInterceptors = canonicalAsyncSites.Select(static (model, _) => CanonicalSyncSourceEmitter.EmitInterceptors(model, "CanonicalAsyncShims"))
            .WithTrackingName("CanonicalAsyncInterceptorsEmission");
        context.RegisterSourceOutput(canonicalAsyncInterceptors, static (ctx, text) =>
        {
            if (text.Length != 0) ctx.AddSource("Zendiator.Canonical.SendAsync.Interceptors.g.cs", SourceText.From(text, Encoding.UTF8));
        });
        var canonicalWrapper=compositionModel.Combine(executionModel).Select(static(input,_)=>CanonicalWrapperEmissionNormalizer.Create(input.Left,input.Right)).WithTrackingName("CanonicalWrapperEmissionModel");
        var wrapperSource=canonicalWrapper.Select(static(model,_)=>CanonicalWrapperSourceEmitter.Emit(model)).WithTrackingName("CanonicalWrapperSourceEmission");
        context.RegisterSourceOutput(wrapperSource,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource("Zendiator.Canonical.Wrappers.g.cs",SourceText.From(text,Encoding.UTF8));});
        var wrapperSites=canonicalWrapper.Combine(compositions).Combine(context.CompilationProvider).Select(static(input,ct)=>CanonicalWrapperEmissionNormalizer.BindCallSites(input.Left.Left,input.Left.Right,input.Right,ct));
        var wrapperInterceptors=wrapperSites.Select(static(model,_)=>CanonicalSyncSourceEmitter.EmitInterceptors(model,"CanonicalWrapperShims"));
        context.RegisterSourceOutput(wrapperInterceptors,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource("Zendiator.Canonical.Wrappers.Interceptors.g.cs",SourceText.From(text,Encoding.UTF8));});
        var capabilities=compositionModel.Combine(executionModel).Combine(canonicalSync).Combine(canonicalAsync).Combine(canonicalWrapper)
            .Select(static(input,_)=>CanonicalCapabilityEmissionNormalizer.Create(input.Left.Left.Left.Left,input.Left.Left.Left.Right,input.Left.Left.Right,input.Left.Right,input.Right)).WithTrackingName("CanonicalCapabilityEmissionModel");
        var capabilitySource=capabilities.Select(static(model,_)=>CanonicalCapabilitySourceEmitter.Emit(model)).WithTrackingName("CanonicalCapabilitySourceEmission");
        context.RegisterSourceOutput(capabilitySource,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource("Zendiator.Canonical.Capabilities.g.cs",SourceText.From(text,Encoding.UTF8));});
        var capabilitySites=capabilities.Combine(compositions).Combine(context.CompilationProvider).Select(static(input,ct)=>CanonicalCapabilityEmissionNormalizer.BindCallSites(input.Left.Left,input.Left.Right,input.Right,ct));
        var capabilityInterceptors=capabilitySites.Select(static(model,_)=>CanonicalSyncSourceEmitter.EmitInterceptors(model,"CanonicalCapabilityShims"));
        context.RegisterSourceOutput(capabilityInterceptors,static(ctx,text)=>{if(text.Length!=0)ctx.AddSource("Zendiator.Canonical.Capabilities.Interceptors.g.cs",SourceText.From(text,Encoding.UTF8));});
        context.RegisterSourceOutput(compositionModel.Combine(executionModel).Combine(compositions).Combine(canonicalSync).Combine(canonicalSyncSites)
            .Combine(canonicalAsync).Combine(canonicalAsyncSites).Combine(canonicalWrapper).Combine(wrapperSites).Combine(capabilities).Combine(capabilitySites), static (ctx, input) =>
        {
            var wrapperInput=input.Left.Left;var original=wrapperInput.Left.Left;var sync = original.Left.Left;
            CompositionSelectionDiscovery.Report(ctx, sync.Left.Left.Left.Left, sync.Left.Left.Right, sync.Left.Left.Left.Right, sync.Left.Right, sync.Right,
                original.Left.Right, original.Right,wrapperInput.Left.Right,wrapperInput.Right,input.Left.Right,input.Right);
        });
        var legacyInvocations = invocations.Combine(compositions)
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
