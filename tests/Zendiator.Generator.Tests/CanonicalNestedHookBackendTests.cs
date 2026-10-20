using Zendiator.SourceGenerator;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly ConcurrentDictionary<bool, SyncFixture> NestedHookBackendFixtures = new();

    private static string NestedHookBackendSource(bool isVoid, string shape = "eligible")
    {
        var result = isVoid ? "" : ",int";
        var afterContract = isVoid ? "ISendSuccessObserver<Ping>" : "ISendResponseTransform<Ping,int>";
        var after = isVoid ? "void ISendSuccessObserver<Ping>.After(Ping request,CancellationToken token)=>_probe.After(_owner,request,0,token);" :
            "int ISendResponseTransform<Ping,int>.After(Ping request,int response,CancellationToken token)=>_probe.After(_owner,request,response,token);";
        var extra = ""; var outer = "Stage.Hook<Hook<Outer>>()"; var inner = "Stage.Hook<Hook<Inner>>()";
        var handler = "typeof(Handler<string>)";
        if (shape is "outer-missing-before" or "inner-missing-finally")
        {
            var contracts = new List<string>(); var methods = new List<string>();
            if (shape != "outer-missing-before") { contracts.Add("ISendRequestTransform<Ping>"); methods.Add("Ping ISendRequestTransform<Ping>.Before(Ping request,CancellationToken token)=>request;"); }
            contracts.Add(afterContract); methods.Add(isVoid ? "void ISendSuccessObserver<Ping>.After(Ping request,CancellationToken token){}" :
                "int ISendResponseTransform<Ping,int>.After(Ping request,int response,CancellationToken token)=>response;");
            contracts.Add("ISendExceptionObserver<Ping>"); methods.Add("void ISendExceptionObserver<Ping>.OnException(Ping request,Exception error,CancellationToken token){}");
            if (shape != "inner-missing-finally") { contracts.Add("ISendFinallyObserver<Ping>"); methods.Add("void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token){}"); }
            extra = "public sealed class PartialHook : " + string.Join(",", contracts) + " { " + string.Join(" ", methods) + " }";
            if (shape == "outer-missing-before") outer = "Stage.Hook<PartialHook>()";
            else inner = "Stage.Hook<PartialHook>()";
        }
        if (shape == "third-hook") inner += ",Stage.Hook<Hook<Third>>()";
        if (shape is "handler-owned" or "handler-borrowed")
        {
            var ownership = shape == "handler-owned" ? "IOwnedSessionFactory" : "IBorrowedSessionFactory";
            extra = "public sealed class HandlerFactory : " + ownership + "<Ping,Handler<string>> { public Handler<string> Create(Ping request,CancellationToken token)=>throw new NotImplementedException(); }";
            handler = "HandlerEntry.Session<Handler<string>,HandlerFactory>()";
        }
        if (shape is "hook-owned" or "hook-borrowed")
        {
            var ownership = shape == "hook-owned" ? "IOwnedSessionFactory" : "IBorrowedSessionFactory";
            extra = "public sealed class HookFactory : " + ownership + "<Ping,Hook<Inner>> { public Hook<Inner> Create(Ping request,CancellationToken token)=>throw new NotImplementedException(); }";
            inner = "Stage.Hook<Hook<Inner>,HookFactory>()";
        }
        if (shape == "wrapper")
        {
            extra = isVoid ? "public sealed class Wrapper : ISendWrapper<Ping> { public void Invoke<TNext>(Ping request,TNext next,CancellationToken token) where TNext:ISendContinuation<Ping> =>next.Invoke(request,token); }" :
                "public sealed class Wrapper : ISendWrapper<Ping,int> { public int Invoke<TNext>(Ping request,TNext next,CancellationToken token) where TNext:ISendContinuation<Ping,int> =>next.Invoke(request,token); }";
            inner = "Stage.Wrapper<Wrapper>()";
        }
        if (shape == "external") inner = isVoid ? "Stage.Hook<global::Zendiator.Generator.Tests.GeneratorTests.ExternalVoidFourPhaseHook<Ping>>()" :
            "Stage.Hook<global::Zendiator.Generator.Tests.GeneratorTests.ExternalFourPhaseHook<Ping,int>>()";
        if (shape == "struct")
        {
            extra = "public struct StructHook : ISendRequestTransform<Ping>," + afterContract + ",ISendExceptionObserver<Ping>,ISendFinallyObserver<Ping> { " +
                "Ping ISendRequestTransform<Ping>.Before(Ping request,CancellationToken token)=>request; " +
                (isVoid ? "void ISendSuccessObserver<Ping>.After(Ping request,CancellationToken token){}" : "int ISendResponseTransform<Ping,int>.After(Ping request,int response,CancellationToken token)=>response;") +
                " void ISendExceptionObserver<Ping>.OnException(Ping request,Exception error,CancellationToken token){} void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token){} }";
            inner = "Stage.Hook<StructHook>()";
        }
        return $$"""
            using System; using System.Threading; using Zendiator; using Zendiator.DependencyInjection; using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.GeneratorTests.NestedSendProbe;
            namespace App {
                public sealed record Ping(string Name) : ISyncRequest{{(isVoid ? "" : "<int>")}};
                public sealed class Outer; public sealed class Inner; public sealed class Third;
                [ZendiatorHandler] public sealed partial class Handler<T> : ISyncRequestHandler<Ping{{result}}>,IDisposable where T:class {
                    private readonly Probe _probe; public Handler(Probe probe) { _probe=probe; probe.Construct("handler"); }
                    {{(isVoid ? "void" : "int")}} ISyncRequestHandler<Ping{{result}}>.Handle(Ping request,CancellationToken token) { {{(isVoid ? "" : "return ")}}_probe.Handle(request,token); }
                    void IDisposable.Dispose()=>_probe.DisposeService("handler");
                }
                public class HookBase<T> : ISendRequestTransform<Ping>,{{afterContract}},ISendExceptionObserver<Ping>,ISendFinallyObserver<Ping>,IDisposable where T:class {
                    private readonly Probe _probe; private readonly string _owner=typeof(T)==typeof(Outer)?"A":"B";
                    public HookBase(Probe probe) { _probe=probe; probe.Construct(_owner); }
                    Ping ISendRequestTransform<Ping>.Before(Ping request,CancellationToken token)=>(Ping)_probe.Before(_owner,request,token)!;
                    {{after}}
                    void ISendExceptionObserver<Ping>.OnException(Ping request,Exception error,CancellationToken token)=>_probe.Observe(_owner,request,error,token);
                    void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token)=>_probe.Finish(_owner,request,exit,token);
                    void IDisposable.Dispose()=>_probe.DisposeService(_owner);
                }
                public sealed class Hook<T> : HookBase<T> where T:class { public Hook(Probe probe):base(probe){} }
                {{extra}}
                [ZendiatorComposition("reads")] public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{
                        c.Handlers<Ping>(DispatchFamily.SendSync,{{handler}}); c.Pipeline<Ping>(DispatchFamily.SendSync,{{outer}},{{inner}});
                    });
                    public static object? ResolveService(IServiceProvider provider)=>new Handler<string>(provider.GetRequiredService<Probe>());
                    {{(shape == "eligible" ? "public static object? Dispatch(object mediator,object? request,CancellationToken token) { " + (isVoid ? "" : "return ") + "((global::App.Generated.Reads.IZendiator)mediator).Send((Ping)request!,token); " + (isVoid ? "return null;" : "") + " }" : "")}}
                }
            }
            """;
    }

    private static SyncFixture NestedHookBackendFixture(bool isVoid) => NestedHookBackendFixtures.GetOrAdd(isVoid,
        key => CompileSyncFixture("direct", key, sourceOverride: NestedHookBackendSource(key)));

    private static Type[] NestedHookFixtureTypes(SyncFixture f)
    {
        var hook = f.Assembly.GetType("App.Hook`1")!;
        return [hook.MakeGenericType(f.Assembly.GetType("App.Outer")!), hook.MakeGenericType(f.Assembly.GetType("App.Inner")!), f.Service];
    }

    private static (object? Result, Exception? Error) InvokeNestedHookReference(SyncFixture f, IServiceProvider provider,
        object? request, CancellationToken token, bool isVoid)
    {
        var types = NestedHookFixtureTypes(f);
        var method = typeof(CanonicalSendSyncReference).GetMethod(isVoid ? "SendTwoHooksVoid" : "SendTwoHooksResponse", BindingFlags.Static | BindingFlags.NonPublic)!;
        method = method.MakeGenericMethod(isVoid ? [f.Request, types[0], types[1], types[2]] : [f.Request, typeof(int), types[0], types[1], types[2]]);
        try { return (method.Invoke(null, [provider, request, token]), null); }
        catch (TargetInvocationException error) { return (null, error.InnerException); }
    }

    private static void WriteNestedHookEvidence(string group, string name, object value)
    {
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(evidence)) return;
        var folder = Path.Combine(Path.GetDirectoryName(evidence)!, group); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(value));
    }

    [Theory]
    [MemberData(nameof(NestedHookReferenceCases))]
    public void Enabled_two_Hooks_SendSync_matches_accepted_reference_and_independent_goldens(bool isVoid, string scenario, string golden)
    {
        var fixture = NestedHookBackendFixture(isVoid);
        var reference = RunNestedHookScenario(isVoid, scenario, golden, fixture);
        var generated = RunNestedHookScenario(isVoid, scenario, golden, fixture, generated: true);
        Assert.Equal(reference, generated);
        WriteNestedHookEvidence("nested-hook-differential", (isVoid ? "void-" : "response-") + scenario,
            new { isVoid, scenario, golden, reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated) });
    }

    public static IEnumerable<object[]> NestedHookLifetimeCases()
    {
        var lifetimes = new[] { ServiceLifetime.Scoped, ServiceLifetime.Transient, ServiceLifetime.Singleton };
        foreach (var isVoid in new[] { false, true }) foreach (var outer in lifetimes) foreach (var inner in lifetimes)
            foreach (var handler in lifetimes) yield return [isVoid, outer, inner, handler];
    }

    [Theory]
    [MemberData(nameof(NestedHookLifetimeCases))]
    public void Enabled_two_Hooks_SendSync_preserves_independent_DI_lifetimes(bool isVoid, ServiceLifetime outer,
        ServiceLifetime inner, ServiceLifetime handler)
    {
        // Lifetime overrides precede AddZendiator's immutable policy snapshot.
        var fixture = NestedHookBackendFixture(isVoid);
        var reference = RunNestedHookLifetimes(isVoid, outer, inner, handler, fixture);
        var generated = RunNestedHookLifetimes(isVoid, outer, inner, handler, fixture, generated: true);
        Assert.Equal(reference, generated);
        WriteNestedHookEvidence("nested-hook-lifetimes", (isVoid ? "void-" : "response-") + outer + "-" + inner + "-" + handler,
            new { isVoid, outer = outer.ToString(), inner = inner.ToString(), handler = handler.ToString(), reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated) });
    }

    [Theory]
    [InlineData(false, "outer-missing-before")]
    [InlineData(true, "outer-missing-before")]
    [InlineData(false, "inner-missing-finally")]
    [InlineData(true, "inner-missing-finally")]
    [InlineData(false, "third-hook")]
    [InlineData(true, "third-hook")]
    [InlineData(false, "handler-owned")]
    [InlineData(true, "handler-owned")]
    [InlineData(false, "handler-borrowed")]
    [InlineData(true, "handler-borrowed")]
    [InlineData(false, "hook-owned")]
    [InlineData(true, "hook-owned")]
    [InlineData(false, "hook-borrowed")]
    [InlineData(true, "hook-borrowed")]
    [InlineData(false, "wrapper")]
    [InlineData(true, "wrapper")]
    [InlineData(false, "external")]
    [InlineData(true, "external")]
    [InlineData(false, "struct")]
    [InlineData(true, "struct")]
    public void Unsupported_two_Hook_shapes_keep_ZEN0037_and_emit_no_backend(bool isVoid, string shape)
    {
        var source = NestedHookBackendSource(isVoid, shape);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "UnsupportedNestedHook_" + shape + isVoid), out var output, out var diagnostics);
        AssertAcceptedSendEmission(driver, diagnostics, BoundDispatchFamily.SendSync, isVoid);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertCanonicalOutputRoles(driver.GetRunResult().Results.Single().GeneratedSources);
        WriteNestedHookEvidence("nested-hook-boundaries", (isVoid ? "void-" : "response-") + shape,
            new { isVoid, shape, source, diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }), generatedExecution = true });
    }

    [Fact]
    public void Enabled_two_Hook_body_edits_cache_source_but_order_and_capability_changes_invalidate_it()
    {
        var source = NestedHookBackendSource(false); var before = SyncCompilation(source, "NestedHookIncremental");
        var driver = SyncDriver().RunGenerators(before);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        var original = CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        var body = before.ReplaceSyntaxTree(before.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(source.Replace("_probe.Before(_owner,request,token)!", "_probe.Before(_owner, request, token)!"), SyncParseOptions));
        driver = driver.RunGenerators(body);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalRouteOutputFile"].SelectMany(s => s.Outputs),
            output => Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        var ordered = body.ReplaceSyntaxTree(body.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(source.Replace("Stage.Hook<Hook<Outer>>(),Stage.Hook<Hook<Inner>>()", "Stage.Hook<Hook<Inner>>(),Stage.Hook<Hook<Outer>>()"), SyncParseOptions));
        driver = driver.RunGenerators(ordered);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        Assert.NotEqual(original, CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
        var capability = ordered.ReplaceSyntaxTree(ordered.SyntaxTrees.Single(), CSharpSyntaxTree.ParseText(NestedHookBackendSource(false, "inner-missing-finally"), SyncParseOptions));
        driver = driver.RunGenerators(capability);
        AssertAcceptedSendEmission(driver, driver.GetRunResult().Diagnostics, BoundDispatchFamily.SendSync, false);
        Assert.NotEqual(original, CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
    }
}
