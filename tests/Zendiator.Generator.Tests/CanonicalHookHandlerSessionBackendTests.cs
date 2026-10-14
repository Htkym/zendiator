using Zendiator.SourceGenerator;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly ConcurrentDictionary<(bool Owned, bool Void), SyncFixture> HookHandlerBackendFixtures = new();
    private static string HookHandlerBackendSource(bool owned, bool isVoid, string shape = "eligible", bool dispatch = true)
    {
        var source = SingleHookBackendSource(isVoid, shape)
            .Replace("GeneratorTests.ReferenceSendProbe", "GeneratorTests.HookHandlerSessionProbe")
            .Replace("_probe.Before", "_probe.Core.Before").Replace("_probe.After", "_probe.Core.After")
            .Replace("_probe.Observe", "_probe.Core.Observe").Replace("_probe.Finish", "_probe.Core.Finish")
            .Replace("_probe.Handle", "_probe.Core.Handle").Replace("_probe.HookDisposals", "_probe.Core.HookDisposals")
            .Replace("probe.ConstructHook()", "probe.Core.ConstructHook()")
            .Replace("private readonly Probe _probe; public Handler(Probe probe) { _probe=probe; probe.ConstructHandler(); }",
                "private readonly Probe _probe; private readonly Ping _request; private readonly CancellationToken _token; public Handler(Probe probe,Ping request,CancellationToken token) { _probe=probe; _request=request; _token=token; probe.Core.ConstructHandler(); }")
            .Replace("void IDisposable.Dispose()=>_probe.Core.HandlerDisposals++;", "void IDisposable.Dispose()=>_probe.Release(this,_request,_token);")
            .Replace("typeof(Handler<string>)", "HandlerEntry.Session<Handler<string>,Factory<string>>()")
            .Replace("public static object? ResolveService(IServiceProvider provider)=>new Handler<string>(provider.GetRequiredService<Probe>());",
                "public static object? ResolveService(IServiceProvider provider) { var probe=provider.GetRequiredService<Probe>(); probe.Core.Phases.Add(\"resolve-factory\"); if(probe.DiError is {} error)throw error; if(probe.NullFactory)return null; return new Factory<string>(probe,provider.GetRequiredService<global::Zendiator.Generator.Tests.GeneratorTests.HookHandlerFactoryDependency>()); }");
        if (!dispatch) source = source.Replace("public static object? Dispatch(object mediator,object? request,CancellationToken token) { " + (isVoid ? "" : "return ") + "((global::App.Generated.Reads.IZendiator)mediator).Send((Ping)request!,token); " + (isVoid ? "return null;" : "") + " }", "");
        var contract = owned ? "IOwnedSessionFactory" : "IBorrowedSessionFactory";
        source += $$"""
            namespace App {
                public sealed class Factory<T> : {{contract}}<Ping,Handler<T>>,IDisposable where T:class {
                    private readonly Probe _probe;
                    public Factory(Probe probe,global::Zendiator.Generator.Tests.GeneratorTests.HookHandlerFactoryDependency dependency) { _probe=probe;probe.Core.Phases.Add("factory.ctor");if(probe.FactoryConstructorError is {} error)throw error; }
                    Handler<T> {{contract}}<Ping,Handler<T>>.Create(Ping request,CancellationToken token)=>_probe.Create(request,token,()=>new Handler<T>(_probe,request,token),{{(owned ? "false" : "true")}});
                    void IDisposable.Dispose(){_probe.Core.Phases.Add("factory.dispose");_probe.FactoryDisposals++;}
                }
            }
            """;
        return source;
    }
    private static SyncFixture HookHandlerBackendFixture(bool owned, bool isVoid) => HookHandlerBackendFixtures.GetOrAdd((owned, isVoid), key =>
        CompileSyncFixture(key.Owned ? "owned" : "borrowed", key.Void, sourceOverride: HookHandlerBackendSource(key.Owned, key.Void)));

    private static (object? Result, Exception? Error) InvokeCompiledHookHandlerReference(SyncFixture fixture, IServiceProvider provider, bool owned, bool isVoid, object? request, CancellationToken token)
    {
        var hook = fixture.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string));
        var handler = fixture.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string));
        var name = owned ? isVoid ? "SendHookOwnedHandlerVoid" : "SendHookOwnedHandlerResponse" : isVoid ? "SendHookBorrowedHandlerVoid" : "SendHookBorrowedHandlerResponse";
        var method = typeof(CanonicalSendSyncReference).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(isVoid ? [fixture.Request, hook, handler, fixture.Service] : [fixture.Request, typeof(int), hook, handler, fixture.Service]);
        try { return (method.Invoke(null, [provider, request, token]), null); }
        catch (TargetInvocationException error) { return (null, error.InnerException); }
    }
    [Theory]
    [MemberData(nameof(HookHandlerReferenceCases))]
    public void Candidate_DI_Hook_Handler_session_matches_reference_and_independent_goldens(bool owned, bool isVoid, string scenario, string golden)
    {
        var fixture = HookHandlerBackendFixture(owned, isVoid);
        var reference = RunHookHandlerScenario(owned, isVoid, scenario, golden, fixture);
        var generated = RunHookHandlerScenario(owned, isVoid, scenario, golden, fixture, generated: true);
        Assert.Equal(reference, generated);
        WriteNestedHookEvidence("hook-handler-session-differential", owned + "-" + isVoid + "-" + scenario, new { owned, isVoid, scenario, golden, reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated) });
    }
    [Theory]
    [MemberData(nameof(HookHandlerReferenceLifetimes))]
    public void Candidate_DI_Hook_Handler_session_matches_reference_DI_and_operation_owners(bool owned, bool isVoid, ServiceLifetime lifetime)
    {
        var fixture = HookHandlerBackendFixture(owned, isVoid);
        var reference = RunHookHandlerLifetimes(owned, isVoid, lifetime, fixture);
        var generated = RunHookHandlerLifetimes(owned, isVoid, lifetime, fixture, generated: true);
        Assert.Equal(reference, generated);
        WriteNestedHookEvidence("hook-handler-session-differential-lifetimes", owned + "-" + isVoid + "-" + lifetime, new { owned, isVoid, lifetime = lifetime.ToString(), reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated) });
    }
    public static IEnumerable<object[]> HookHandlerBackendBoundaries()
    {
        foreach (var owned in new[] { false, true }) foreach (var isVoid in new[] { false, true }) foreach (var shape in new[] { "two-hooks", "hook-owned", "wrapper", "missing-before", "missing-after", "missing-exception", "missing-finally", "async" })
            yield return [owned, isVoid, shape];
    }
    [Theory]
    [MemberData(nameof(HookHandlerBackendBoundaries))]
    public void Hook_Handler_session_candidate_blocks_unproved_combinations(bool owned, bool isVoid, string shape)
    {
        var source = HookHandlerBackendSource(owned, isVoid, shape == "async" ? "eligible" : shape, dispatch: false);
        if (shape == "async")
        {
            source = source.Replace("using System;", "using System; using System.Threading.Tasks;")
            .Replace("ISyncRequest", "IRequest").Replace("DispatchFamily.SendSync", "DispatchFamily.SendAsync")
            .Replace((isVoid ? "void" : "int") + " IRequestHandler<Ping" + (isVoid ? "" : ",int") + ">.Handle(Ping request,CancellationToken token) { " + (isVoid ? "" : "return ") + "_probe.Core.Handle(request,token); }",
                (isVoid ? "ValueTask" : "ValueTask<int>") + " IRequestHandler<Ping" + (isVoid ? "" : ",int") + ">.HandleAsync(Ping request,CancellationToken token) { " + (isVoid ? "_probe.Core.Handle(request,token); return default;" : "return new(_probe.Core.Handle(request,token));") + " }");
            if (owned) source = source.Replace("IRequestHandler<Ping" + (isVoid ? "" : ",int") + ">,IDisposable where T:class", "IRequestHandler<Ping" + (isVoid ? "" : ",int") + ">,IDisposable,IAsyncDisposable where T:class")
                .Replace("void IDisposable.Dispose()=>_probe.Release(this,_request,_token);", "void IDisposable.Dispose()=>_probe.Release(this,_request,_token); public ValueTask DisposeAsync(){_probe.Release(this,_request,_token);return default;}");
        }
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "HookHandlerBoundary" + Guid.NewGuid()), out var output, out var diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        AssertAcceptedSendEmission(driver, diagnostics, shape == "async" ? BoundDispatchFamily.SendAsync : BoundDispatchFamily.SendSync, isVoid);
        var generatedCount = driver.GetRunResult().Results.Single().GeneratedSources.Length; AssertCanonicalOutputRoles(driver.GetRunResult().Results.Single().GeneratedSources);
        WriteNestedHookEvidence("hook-handler-session-boundaries", owned + "-" + isVoid + "-" + shape, new { owned, isVoid, shape, source, diagnostics = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }), generatedSources = generatedCount });
    }
    [Fact]
    public void Hook_Handler_session_body_edits_cache_emission_but_ownership_changes_cleanup()
    {
        var source = HookHandlerBackendSource(true, false, dispatch: false);
        var driver = SyncDriver().RunGenerators(SyncCompilation(source, "HookHandlerIncremental"));
        var first = CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        driver = driver.RunGenerators(SyncCompilation(source.Replace("return _probe.Core.Handle(request,token);", "return _probe.Core.Handle(request,token)+0;"), "HookHandlerIncremental"));
        Assert.Equal(first, CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalRouteOutputFile"].SelectMany(s => s.Outputs), o => Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        driver = driver.RunGenerators(SyncCompilation(HookHandlerBackendSource(false, false, dispatch: false), "HookHandlerIncremental"));
        var borrowed = CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        Assert.NotEqual(first, borrowed); Assert.Contains("var detached = handler;", first); Assert.DoesNotContain("var detached = handler;", borrowed);
        WriteNestedHookEvidence("hook-handler-session-incremental", "ownership", new { bodyCached = true, ownershipChangesCleanup = true });
    }
}
