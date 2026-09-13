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
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Zendiator.GenerateZendiatorAttribute",
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol).Collect();
        var invocations = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name: IdentifierNameSyntax { Identifier.Text: "AddZendiator" } }
            },
            static (ctx, _) => (InvocationExpressionSyntax)ctx.Node).Collect();
        var result = declarations.Combine(invocations).Combine(context.CompilationProvider)
            .Select(static (input, ct) => new GenerationAnalysis(input.Left.Left, input.Left.Right, input.Right, ct).Generate()).WithTrackingName("Analysis");

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
