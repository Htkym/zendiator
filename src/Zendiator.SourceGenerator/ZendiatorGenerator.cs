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
        context.RegisterSourceOutput(compositionModel.Combine(executionModel).Combine(compositions).Combine(canonicalSync), static (ctx, input) =>
            CompositionSelectionDiscovery.Report(ctx, input.Left.Left.Left, input.Left.Right, input.Left.Left.Right, input.Right));
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
