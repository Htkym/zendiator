using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string InvokerBefore = "public ValueTask<Ping> BeforeAsync(Ping r,CancellationToken t){_probe.CheckToken(t);return _probe.Before(_id,r,new Ping(r.Value+1));}";

    private static string AsyncInvokerSource(string shape)
    {
        var source = AsyncHookSource("direct", false)
            .Replace("[ZendiatorHandler]public sealed class Handler:", "public class HandlerBase:")
            .Replace("public Handler(Probe p)", "public HandlerBase(Probe p)")
            .Replace("ValueTask<int> IRequestHandler<Ping,int>.HandleAsync", "public ValueTask<int> HandleAsync");
        source += "namespace App{[ZendiatorHandler]public sealed class Handler:HandlerBase{public Handler(Probe p):base(p){}}}";
        if (shape == "explicit")
            return source.Replace("public Hook(Probe p)", "public ValueTask<Ping> BeforeAsync(Ping r,CancellationToken t)=>throw new InvalidOperationException(\"Wrong public overload\");public Hook(Probe p)");
        source = source.Replace("ValueTask<Ping> IAsyncSendRequestTransform<Ping>.BeforeAsync", "public ValueTask<Ping> BeforeAsync")
            .Replace("ValueTask<int> IAsyncSendResponseTransform<Ping,int>.AfterAsync", "public ValueTask<int> AfterAsync")
            .Replace("ValueTask IAsyncSendExceptionObserver<Ping>.OnExceptionAsync", "public ValueTask OnExceptionAsync")
            .Replace("ValueTask IAsyncSendFinallyObserver<Ping>.FinallyAsync", "public ValueTask FinallyAsync")
            .Replace("private readonly Probe _probe;private readonly int _id;", "protected readonly Probe _probe;protected readonly int _id;");
        Assert.Contains(InvokerBefore, source);
        if (shape is "exact" or "derived" or "virtual")
        {
            source = source.Replace("public sealed class Hook<T>", "public class Hook<T>");
            if (shape == "exact")
                return source.Replace("public Hook(Probe p)", "public new Type GetType()=>throw new InvalidOperationException(\"User GetType\");public Hook(Probe p)");
            var virtualModifier = shape == "virtual" ? "virtual " : "";
            source = source.Replace(InvokerBefore, "public " + virtualModifier + "ValueTask<Ping> BeforeAsync(Ping r,CancellationToken t)=>throw new InvalidOperationException(\"Wrong base dispatch\");")
                .Replace("services.AddZendiator<Reads>", "services.AddScoped<Hook<OuterTag>,DerivedHook<OuterTag>>().AddZendiator<Reads>");
            return source + "namespace App{public sealed class DerivedHook<T>:Hook<T>" + (shape == "derived" ? ",IAsyncSendRequestTransform<Ping>" : "") +
                " where T:class{public DerivedHook(Probe p):base(p){}" + InvokerBefore.Replace("public ValueTask", "public " + (shape == "virtual" ? "override" : "new") + " ValueTask") + "}}";
        }
        source = source.Replace("public sealed class Hook<T>", "public class HookBase<T>").Replace("public Hook(Probe p)", "public HookBase(Probe p)");
        var extra = "";
        if (shape == "obsolete")
            source = source.Replace(InvokerBefore, "[Obsolete(\"Call through the contract\",true)]" + InvokerBefore);
        if (shape == "priority")
            source = source.Replace("public HookBase(Probe p)",
            "[System.Runtime.CompilerServices.OverloadResolutionPriority(1)]public ValueTask<Ping> BeforeAsync(object r,CancellationToken t)=>throw new InvalidOperationException(\"Wrong priority overload\");public HookBase(Probe p)");
        if (shape == "hidden")
            extra = "public new ValueTask<Ping> BeforeAsync(Ping r,CancellationToken t)=>throw new InvalidOperationException(\"Hidden method is not the contract\");";
        if (shape == "multiple")
        {
            source = source.Replace("public class HookBase<T>:", "public class HookBase<T>:IAsyncSendRequestTransform<Other>,");
            extra = "public ValueTask<Other> BeforeAsync(Other r,CancellationToken t)=>throw new InvalidOperationException(\"Wrong request contract\");";
            source = source.Replace("public HookBase(Probe p)", extra + "public HookBase(Probe p)");
            extra = "";
            source += "namespace App{public sealed record Other:IRequest<int>;}";
        }
        if (shape == "reimplemented")
            source = source.Replace(InvokerBefore, "public ValueTask<Ping> BeforeAsync(Ping r,CancellationToken t)=>throw new InvalidOperationException(\"Wrong inherited contract\");");
        return source + "namespace App{public sealed class Hook<T>:HookBase<T>" + (shape == "reimplemented" ? ",IAsyncSendRequestTransform<Ping>" : "") +
            " where T:class{public Hook(Probe p):base(p){}" + extra + (shape == "reimplemented" ? InvokerBefore.Replace("public ValueTask", "public new ValueTask") : "") + "}}";
    }

    private static AsyncHookFixture CompileAsyncInvokerFixture(string shape)
    {
        var source = AsyncInvokerSource(shape);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AsyncInvoker_" + shape), out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var sources = driver.GetRunResult().Results.Single().GeneratedSources;
        AssertCanonicalOutputRoles(sources);
        var route = Assert.Single(sources, s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal));
        var before = Assert.Single(route.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>(), m => m.Identifier.ValueText == "Before0");
        var model = output.GetSemanticModel(route.SyntaxTree);
        var concrete = before.DescendantNodes().OfType<InvocationExpressionSyntax>().Select(n => model.GetSymbolInfo(n).Symbol).OfType<IMethodSymbol>()
            .Where(m => m.Name == "BeforeAsync" && m.ContainingType.TypeKind == TypeKind.Class).ToArray();
        var receiver = output.GetTypeByMetadataName("App.Hook`1")!.Construct(output.GetTypeByMetadataName("App.OuterTag")!);
        var contract = receiver.AllInterfaces.Single(i => i.OriginalDefinition.MetadataName == "IAsyncSendRequestTransform`1" && i.TypeArguments[0].Name == "Ping");
        var expected = receiver.FindImplementationForInterfaceMember(contract.GetMembers("BeforeAsync").Single());
        if (shape is "explicit" or "obsolete" or "priority")
            Assert.Empty(concrete);
        else
            Assert.True(SymbolEqualityComparer.Default.Equals(expected, Assert.Single(concrete)), "Concrete callback differs from the selected interface implementation.");
        Assert.Equal(shape is "exact" or "derived" or "virtual", before.ToString().Contains("GetType()", StringComparison.Ordinal));
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var p = Path.Combine(evidence, "typed-invoker-" + shape);
            Directory.CreateDirectory(p);
            File.WriteAllText(Path.Combine(p, "input.cs"), source);
            foreach (var s in sources)
                File.WriteAllText(Path.Combine(p, s.HintName), s.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(p, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(p, "binding.json"), JsonSerializer.Serialize(new
            {
                shape,
                expected = expected!.ToDisplayString(),
                concrete = concrete.Select(m => m.ToDisplayString()),
                exactGuard = shape is "exact" or "derived" or "virtual",
                compiled = emitted.Success
            }));
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var bridge = assembly.GetType("App.Bridge")!;
        var outer = assembly.GetType("App.Hook`1")!.MakeGenericType(assembly.GetType("App.OuterTag")!);
        return new(assembly, assembly.GetType("App.Generated.Reads.Zendiator")!, assembly.GetType("App.Ping")!, outer,
            assembly.GetType("App.Hook`1")!.MakeGenericType(assembly.GetType("App.InnerTag")!), assembly.GetType("App.Handler")!, outer,
            bridge.GetMethod("Register")!, bridge.GetMethod("Dispatch")!);
    }

    [Theory]
    [InlineData("inherited")]
    [InlineData("hidden")]
    [InlineData("reimplemented")]
    [InlineData("multiple")]
    [InlineData("explicit")]
    [InlineData("exact")]
    [InlineData("derived")]
    [InlineData("virtual")]
    [InlineData("obsolete")]
    [InlineData("priority")]
    public async Task Async_typed_invoker_binds_the_contract_and_preserves_pending_context_and_failure(string shape)
    {
        var fixture = CompileAsyncInvokerFixture(shape);
        foreach (var generated in new[] { false, true })
        {
            await RunAsyncHookCase(fixture, "direct", false, generated, "success", "h0.before");
            await RunAsyncHookCase(fixture, "direct", false, generated, "observer-pending-oce", "h0.exception");
            await RunAsyncHookCase(fixture, "direct", false, generated, "native-before");
            if (shape == "inherited")
                await RunAsyncHookCase(fixture, "direct", false, generated, "success", "handler");
        }
    }

    [Fact]
    public void Async_typed_invoker_binding_is_equatable_and_sealed_changes_invalidate_the_route()
    {
        var source = AsyncInvokerSource("inherited");
        var driver = SyncDriver().RunGenerators(SyncCompilation(source, "InvokerIncremental"));
        var body = CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        driver = driver.RunGenerators(SyncCompilation(source.Replace("r.Value)", "r.Value+1)"), "InvokerIncremental"));
        Assert.Equal(body, CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalRouteOutputFile"].SelectMany(s => s.Outputs), o => Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        driver = driver.RunGenerators(SyncCompilation(source.Replace("public sealed class Hook<T>", "public class Hook<T>"), "InvokerIncremental"));
        Assert.NotEqual(body, CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
        Assert.Contains("GetType()", CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
    }
}
