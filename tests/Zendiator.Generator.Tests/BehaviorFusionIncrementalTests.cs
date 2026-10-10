using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class BehaviorFusionIncrementalTests
{
    [Fact]
    public void Same_driver_reuses_normalized_plans_but_invalidates_changed_capability()
    {
        const string source = "namespace App; public class Stage { public void Before() { } }";
        var parse = new CSharpParseOptions(LanguageVersion.Preview);
        var input = CSharpCompilation.Create("FusionFixture", [CSharpSyntaxTree.ParseText(source, parse)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new PlanFixture().AsSourceGenerator()],
            parseOptions: parse, driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, true));
        driver = driver.RunGenerators(input);
        var initial = driver.GetRunResult().GeneratedTrees.Single().ToString();
        input = input.AddSyntaxTrees(CSharpSyntaxTree.ParseText("namespace App; class Unrelated {}", parse));
        driver = driver.RunGenerators(input);
        Assert.Equal(initial, driver.GetRunResult().GeneratedTrees.Single().ToString());
        AssertReason(driver, IncrementalStepRunReason.Cached);
        input = input.ReplaceSyntaxTree(input.SyntaxTrees.First(), CSharpSyntaxTree.ParseText(source.Replace("{ }", "{ var value = 42; }"), parse));
        driver = driver.RunGenerators(input);
        Assert.Equal(initial, driver.GetRunResult().GeneratedTrees.Single().ToString());
        AssertReason(driver, IncrementalStepRunReason.Cached);
        input = input.ReplaceSyntaxTree(input.SyntaxTrees.First(), CSharpSyntaxTree.ParseText(source.Replace("Before", "After"), parse));
        driver = driver.RunGenerators(input);
        Assert.NotEqual(initial, driver.GetRunResult().GeneratedTrees.Single().ToString());
        AssertReason(driver, IncrementalStepRunReason.Modified);
    }

    private static void AssertReason(GeneratorDriver driver, IncrementalStepRunReason reason) =>
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["FusionPlan"].SelectMany(s => s.Outputs),
            output => Assert.Equal(reason, output.Reason));

    // A provider fixture supplies bound capability facts. This is not the production Handler
    // discovery/parser or a claim that H04/H05 front-end invalidation has been implemented.
    private sealed class PlanFixture : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var inputs = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { Identifier.ValueText: "Stage" },
                static (ctx, _) => Normalize((ClassDeclarationSyntax)ctx.Node));
            var plans = inputs.Select(static (input, _) => BehaviorFusionPlanner.Create(input)).WithTrackingName("FusionPlan");
            context.RegisterSourceOutput(plans, static (ctx, plan) =>
                ctx.AddSource("FusionPlan.g.cs", SourceText.From("// " + plan.Core.StageCapabilities[0] + "\n", Encoding.UTF8)));
        }

        private static FusionPlanInput Normalize(ClassDeclarationSyntax stage)
        {
            var capability = stage.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.ValueText == "After")
                ? FusionCapabilities.Success : FusionCapabilities.Before;
            var type = new EmissionType("global::App.Stage", "global::App.Stage", true, false);
            return BehaviorFusionPlannerTests.Input(FusionDispatchKind.SendAsync,
                new FusionDeclaredStage(type, FusionStageKind.Hook, capability));
        }
    }
}
