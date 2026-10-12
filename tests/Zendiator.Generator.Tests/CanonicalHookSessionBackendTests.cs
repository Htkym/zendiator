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
    private static readonly ConcurrentDictionary<(bool Owned, bool Void), SyncFixture> HookSessionBackendFixtures = new();
    private static string HookSessionBackendSource(bool owned, bool isVoid, bool dispatch = true)
    {
        var ownership = owned ? "IOwnedSessionFactory" : "IBorrowedSessionFactory";
        var afterContract = isVoid ? "ISendSuccessObserver<Ping>" : "ISendResponseTransform<Ping,int>";
        var after = isVoid ? "void ISendSuccessObserver<Ping>.After(Ping request,CancellationToken token) { _probe.HookReceivers.Add(this); _probe.Core.After(request,0,token); }" :
            "int ISendResponseTransform<Ping,int>.After(Ping request,int response,CancellationToken token) { _probe.HookReceivers.Add(this); return _probe.Core.After(request,response,token); }";
        return $$"""
            using System; using System.Threading; using Zendiator; using Zendiator.DependencyInjection; using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.GeneratorTests.HookSessionReferenceProbe;
            using Dependency=global::Zendiator.Generator.Tests.GeneratorTests.HookFactoryDependency;
            namespace App {
                public sealed record Ping : ISyncRequest{{(isVoid ? "" : "<int>")}};
                [ZendiatorHandler] public sealed partial class Handler<T> : ISyncRequestHandler<Ping{{(isVoid ? "" : ",int")}}>,IDisposable where T:class {
                    private readonly Probe _probe; public Handler(Probe probe) { _probe=probe; probe.Core.ConstructHandler(); }
                    {{(isVoid ? "void" : "int")}} ISyncRequestHandler<Ping{{(isVoid ? "" : ",int")}}>.Handle(Ping request,CancellationToken token) { {{(isVoid ? "" : "return ")}}_probe.Core.Handle(request,token); }
                    void IDisposable.Dispose()=>_probe.Core.HandlerDisposals++;
                }
                public class HookBase<T> : ISendRequestTransform<Ping>,{{afterContract}},ISendExceptionObserver<Ping>,ISendFinallyObserver<Ping>,IDisposable where T:class {
                    private readonly Probe _probe; public HookBase(Probe probe) { _probe=probe; probe.Core.ConstructHook(); }
                    Ping ISendRequestTransform<Ping>.Before(Ping request,CancellationToken token) { _probe.HookReceivers.Add(this); return (Ping)_probe.Core.Before(request,token)!; }
                    {{after}}
                    void ISendExceptionObserver<Ping>.OnException(Ping request,Exception error,CancellationToken token) { _probe.HookReceivers.Add(this); _probe.Core.Observe(request,error,token); }
                    void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token) { _probe.HookReceivers.Add(this); _probe.Core.Finish(request,exit,token); }
                    void IDisposable.Dispose()=>_probe.Release(this);
                }
                public sealed class Hook<T> : HookBase<T> where T:class { public Hook(Probe probe):base(probe){} }
                public class FactoryBase<T> : {{ownership}}<Ping,Hook<T>>,IDisposable where T:class {
                    private readonly Probe _probe;
                    public FactoryBase(Probe probe,Dependency dependency) { _probe=probe; probe.Core.Phases.Add("factory.ctor"); if (probe.FactoryConstructorError is {} error) throw error; }
                    Hook<T> {{ownership}}<Ping,Hook<T>>.Create(Ping request,CancellationToken token)=>_probe.Create(this,request,token,()=>new Hook<T>(_probe),{{(owned ? "false" : "true")}});
                    void IDisposable.Dispose() { _probe.Core.Phases.Add("factory.dispose"); _probe.FactoryDisposals++; }
                }
                public sealed class Factory<T> : FactoryBase<T> where T:class { public Factory(Probe probe,Dependency dependency):base(probe,dependency){} }
                [ZendiatorComposition("reads")] public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=> {
                        c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler<string>));
                        c.Pipeline<Ping>(DispatchFamily.SendSync,Stage.Hook<Hook<string>,Factory<string>>());
                    });
                    public static object? ResolveService(IServiceProvider provider) {
                        var probe=provider.GetRequiredService<Probe>(); probe.Core.Phases.Add("factory.resolve");
                        if (probe.DiError is {} error) throw error; if (probe.NullFactory) return null;
                        return new Factory<string>(probe,provider.GetRequiredService<Dependency>());
                    }
                    {{(dispatch ? "public static object? Dispatch(object mediator,object? request,CancellationToken token) { " + (isVoid ? "" : "return ") + "((global::App.Generated.Reads.IZendiator)mediator).Send((Ping)request!,token);" + (isVoid ? "return null;" : "") + " }" : "")}}
                }
            }
            """;
    }
    private static SyncFixture HookSessionBackendFixture(bool owned, bool isVoid) => HookSessionBackendFixtures.GetOrAdd((owned, isVoid), key =>
        CompileSyncFixture(key.Owned ? "owned" : "borrowed", key.Void, sourceOverride: HookSessionBackendSource(key.Owned, key.Void)));

    private static (object? Result, Exception? Error) InvokeCompiledHookSessionReference(SyncFixture fixture, IServiceProvider provider,
        bool owned, bool isVoid, object? request, CancellationToken token)
    {
        var hook = fixture.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string));
        var handler = fixture.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string));
        var name = owned ? isVoid ? "SendOwnedHookVoid" : "SendOwnedHookResponse" : isVoid ? "SendBorrowedHookVoid" : "SendBorrowedHookResponse";
        var method = typeof(CanonicalSendSyncReference).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(isVoid ? [fixture.Request, hook, handler, fixture.Service] : [fixture.Request, typeof(int), hook, handler, fixture.Service]);
        try { return (method.Invoke(null, [provider, request, token]), null); }
        catch (TargetInvocationException error) { return (null, error.InnerException); }
    }

    [Theory]
    [MemberData(nameof(HookSessionReferenceCases))]
    public void Candidate_single_Hook_session_matches_accepted_reference_and_independent_goldens(bool owned, bool isVoid, string scenario, string golden)
    {
        var fixture = HookSessionBackendFixture(owned, isVoid);
        var reference = RunHookSessionScenario(owned, isVoid, scenario, golden, fixture);
        var generated = RunHookSessionScenario(owned, isVoid, scenario, golden, fixture, generated: true);
        Assert.Equal(reference, generated);
        WriteNestedHookEvidence("hook-session-differential", owned + "-" + isVoid + "-" + scenario, new {
            owned, isVoid, scenario, golden, reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated)
        });
    }
    [Theory]
    [MemberData(nameof(HookSessionReferenceLifetimeCases))]
    public void Candidate_single_Hook_session_matches_reference_factory_Handler_and_transfer_owners(bool owned, bool isVoid, ServiceLifetime lifetime)
    {
        var fixture = HookSessionBackendFixture(owned, isVoid);
        var reference = RunHookSessionLifetimes(owned, isVoid, lifetime, fixture);
        var generated = RunHookSessionLifetimes(owned, isVoid, lifetime, fixture, generated: true);
        Assert.Equal(reference, generated);
        WriteNestedHookEvidence("hook-session-differential-lifetimes", owned + "-" + isVoid + "-" + lifetime, new {
            owned, isVoid, lifetime = lifetime.ToString(), reference = JsonSerializer.Deserialize<JsonElement>(reference), generated = JsonSerializer.Deserialize<JsonElement>(generated)
        });
    }

    public sealed class ExternalResponseSessionHook<R,T> : ISendRequestTransform<R>, ISendResponseTransform<R,T>, ISendExceptionObserver<R>, ISendFinallyObserver<R>, IDisposable
    {
        public R Before(R request,CancellationToken token)=>request;
        public T After(R request,T response,CancellationToken token)=>response;
        public void OnException(R request,Exception error,CancellationToken token){}
        public void Finally(R request,SendExitInfo exit,CancellationToken token){}
        public void Dispose(){}
    }
    public sealed class ExternalVoidSessionHook<R> : ISendRequestTransform<R>, ISendSuccessObserver<R>, ISendExceptionObserver<R>, ISendFinallyObserver<R>, IDisposable
    {
        public R Before(R request,CancellationToken token)=>request;
        public void After(R request,CancellationToken token){}
        public void OnException(R request,Exception error,CancellationToken token){}
        public void Finally(R request,SendExitInfo exit,CancellationToken token){}
        public void Dispose(){}
    }
    public static IEnumerable<object[]> HookSessionBackendBoundaries()
    {
        foreach (var owned in new[] { false, true }) foreach (var isVoid in new[] { false, true }) foreach (var shape in new[] {
            "two-hooks", "handler-session", "factory-struct", "factory-internal", "missing-before", "missing-after", "missing-exception", "missing-finally", "external-hook", "async" })
            yield return [owned, isVoid, shape];
    }
    [Theory]
    [MemberData(nameof(HookSessionBackendBoundaries))]
    public void Hook_session_candidate_blocks_unproved_shapes_before_emission(bool owned, bool isVoid, string shape)
    {
        var source = HookSessionBackendSource(owned, isVoid, dispatch: false);
        if (shape == "two-hooks") source = source.Replace("Stage.Hook<Hook<string>,Factory<string>>()", "Stage.Hook<Hook<string>,Factory<string>>(),Stage.Hook<SecondHook>()") +
            "namespace App { public sealed class SecondHook : HookBase<string> { public SecondHook(Probe probe):base(probe){} } }";
        if (shape == "handler-session") source = source.Replace("typeof(Handler<string>)", "HandlerEntry.Session<Handler<string>,HandlerFactory>()") +
            "namespace App { public sealed class HandlerFactory : IOwnedSessionFactory<Ping,Handler<string>> { public Handler<string> Create(Ping r,CancellationToken t)=>throw new NotImplementedException(); } }";
        if (shape == "factory-internal") source = source.Replace("public sealed class Factory<T>", "internal sealed class Factory<T>");
        if (shape == "factory-struct") source = source.Replace("public sealed class Factory<T> : FactoryBase<T> where T:class { public Factory(Probe probe,Dependency dependency):base(probe,dependency){} }",
            "public struct Factory<T> : " + (owned ? "IOwnedSessionFactory" : "IBorrowedSessionFactory") + "<Ping,Hook<T>> where T:class { public Factory(Probe p,Dependency d){} public Hook<T> Create(Ping r,CancellationToken t)=>throw new NotImplementedException(); }");
        if (shape == "missing-before") source = source.Replace("ISendRequestTransform<Ping>,", "").Replace("Ping ISendRequestTransform<Ping>.Before(Ping request,CancellationToken token) { _probe.HookReceivers.Add(this); return (Ping)_probe.Core.Before(request,token)!; }", "");
        if (shape == "missing-after") source = source.Replace(isVoid ? "ISendSuccessObserver<Ping>," : "ISendResponseTransform<Ping,int>,", "").Replace(isVoid ?
            "void ISendSuccessObserver<Ping>.After(Ping request,CancellationToken token) { _probe.HookReceivers.Add(this); _probe.Core.After(request,0,token); }" :
            "int ISendResponseTransform<Ping,int>.After(Ping request,int response,CancellationToken token) { _probe.HookReceivers.Add(this); return _probe.Core.After(request,response,token); }", "");
        if (shape == "missing-exception") source = source.Replace("ISendExceptionObserver<Ping>,", "").Replace("void ISendExceptionObserver<Ping>.OnException(Ping request,Exception error,CancellationToken token) { _probe.HookReceivers.Add(this); _probe.Core.Observe(request,error,token); }", "");
        if (shape == "missing-finally") source = source.Replace("ISendFinallyObserver<Ping>,", "").Replace("void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token) { _probe.HookReceivers.Add(this); _probe.Core.Finish(request,exit,token); }", "");
        if (shape == "external-hook")
        {
            var external = "global::Zendiator.Generator.Tests.GeneratorTests." + (isVoid ? "ExternalVoidSessionHook<Ping>" : "ExternalResponseSessionHook<Ping,int>");
            source = source.Replace("Stage.Hook<Hook<string>,Factory<string>>()", "Stage.Hook<" + external + ",ExternalFactory>()") +
                "namespace App { public sealed class ExternalFactory : " + (owned ? "IOwnedSessionFactory" : "IBorrowedSessionFactory") + "<Ping," + external + "> { public " + external + " Create(Ping r,CancellationToken t)=>throw new NotImplementedException(); } }";
        }
        if (shape == "async")
        {
            source = source.Replace("using System;", "using System; using System.Threading.Tasks;")
                .Replace("ISyncRequest", "IRequest").Replace("DispatchFamily.SendSync", "DispatchFamily.SendAsync")
                .Replace((isVoid ? "void" : "int") + " IRequestHandler<Ping" + (isVoid ? "" : ",int") + ">.Handle(Ping request,CancellationToken token) { " + (isVoid ? "" : "return ") + "_probe.Core.Handle(request,token); }",
                    (isVoid ? "ValueTask" : "ValueTask<int>") + " IRequestHandler<Ping" + (isVoid ? "" : ",int") + ">.HandleAsync(Ping request,CancellationToken token) { " + (isVoid ? "_probe.Core.Handle(request,token); return default;" : "return new(_probe.Core.Handle(request,token));") + " }");
            if (owned) source = source.Replace("ISendFinallyObserver<Ping>,IDisposable where", "ISendFinallyObserver<Ping>,IDisposable,IAsyncDisposable where")
                .Replace("void IDisposable.Dispose()=>_probe.Release(this);", "void IDisposable.Dispose()=>_probe.Release(this); public ValueTask DisposeAsync(){ _probe.Release(this); return default; }");
        }
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"HookSessionBoundary" + Guid.NewGuid()),out var output,out var diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.Equal("ZEN0037",Assert.Single(diagnostics).Id);
        var generatedCount=driver.GetRunResult().Results.Single().GeneratedSources.Length;
        Assert.Equal(shape=="handler-session"?2:0,generatedCount);
        WriteNestedHookEvidence("hook-session-boundaries",owned + "-" + isVoid + "-" + shape,new { owned,isVoid,shape,source,
            diagnostics=diagnostics.Select(d=>new { d.Id,message=d.GetMessage() }),generatedSources=generatedCount });
    }

    [Fact]
    public void Hook_session_body_edits_cache_emission_but_ownership_changes_invalidate_cleanup()
    {
        var source = HookSessionBackendSource(true,false,dispatch:false);
        var driver = SyncDriver().RunGenerators(SyncCompilation(source,"HookSessionIncremental"));
        var first = driver.GetRunResult().Results.Single().GeneratedSources.First(s=>s.HintName=="Zendiator.Canonical.SendSync.g.cs").SourceText.ToString();
        driver = driver.RunGenerators(SyncCompilation(source.Replace("return _probe.Core.After(request,response,token);","return _probe.Core.After(request,response,token)+0;"),"HookSessionIncremental"));
        Assert.Equal(first,driver.GetRunResult().Results.Single().GeneratedSources.First(s=>s.HintName=="Zendiator.Canonical.SendSync.g.cs").SourceText.ToString());
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalSyncSourceEmission"].SelectMany(s=>s.Outputs),o=>Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        driver = driver.RunGenerators(SyncCompilation(HookSessionBackendSource(false,false,dispatch:false),"HookSessionIncremental"));
        var borrowed = driver.GetRunResult().Results.Single().GeneratedSources.First(s=>s.HintName=="Zendiator.Canonical.SendSync.g.cs").SourceText.ToString();
        Assert.NotEqual(first,borrowed); Assert.Contains("var detached = hook;",first); Assert.DoesNotContain("var detached = hook;",borrowed);
        WriteNestedHookEvidence("hook-session-incremental","ownership",new { bodyCached=true,ownershipChangesCleanup=true });
    }
}
