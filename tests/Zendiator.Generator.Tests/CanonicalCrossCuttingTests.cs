using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record CrossFixture(Assembly Assembly, Type Mediator, Type Request, MethodInfo Register, MethodInfo Dispatch, MethodInfo Reference);
    private static readonly ConcurrentDictionary<string, Lazy<CrossFixture>> CrossFixtures = new();
    private static string CrossSource(string kind)
    {
        var async = kind != "context-sync"; var factoryAsync = kind is "life" or "concurrent"; var concurrent = kind == "concurrent";
        var result = async ? "ValueTask<int>" : "int"; var prefix = async ? "IAsyncSend" : "ISend"; var disposable = async ? "IAsyncDisposable" : "IDisposable"; var create = factoryAsync ? "CreateAsync" : "Create"; var role = factoryAsync ? "IAsyncOwnedSessionFactory" : "IOwnedSessionFactory";
        string KindExpression = "typeof(T)==typeof(OuterTag)?\"outer\":typeof(T)==typeof(InnerTag)?\"inner\":\"k\"";
        var hook = $$"""
            public sealed class K<T>:{{prefix}}RequestTransform<Ping>,{{prefix}}ResponseTransform<Ping,int>,{{prefix}}ExceptionObserver<Ping>,{{prefix}}FinallyObserver<Ping>,{{disposable}} where T:class{
                private readonly Probe _p;private readonly Ping _r;private readonly int _id;private readonly string _kind;
                public K(Probe p,Ping r){_p=p;_r=r;_kind={{KindExpression}};_id=p.Create(this,_kind,r);}
                public {{(async ? "ValueTask<Ping>" : "Ping")}} {{(async ? "BeforeAsync" : "Before")}}(Ping r,CancellationToken t){var next=r with{Value=r.Value+1,Stamp=_id};{{(async ? "return _p.Value(_kind+\".before\",r,_id,next);" : "_p.Sync(_kind+\".before\",r,_id);return next;")}}}
                public {{result}} {{(async ? "AfterAsync" : "After")}}(Ping r,int value,CancellationToken t){_p.Check(_id,r);{{(async ? "return _p.Value(_kind+\".after\",r,_id,value+1);" : "_p.Sync(_kind+\".after\",r,_id);return value+1;")}}}
                public {{(async ? "ValueTask" : "void")}} {{(async ? "OnExceptionAsync" : "OnException")}}(Ping r,Exception e,CancellationToken t){_p.Check(_id,r);{{(async ? "return _p.Void(_kind+\".exception\",r,_id,e);" : "_p.Sync(_kind+\".exception\",r,_id,e);")}}}
                public {{(async ? "ValueTask" : "void")}} {{(async ? "FinallyAsync" : "Finally")}}(Ping r,SendExitInfo e,CancellationToken t){_p.Check(_id,r);{{(async ? "return _p.Void(_kind+\".finally\",r,_id,exit:e);" : "_p.Sync(_kind+\".finally\",r,_id,exit:e);")}}}
                public {{(async ? "ValueTask DisposeAsync()" : "void Dispose()")}}{_p.Release(this);{{(async ? "return _p.Void(_kind+\".cleanup\",_r,_id);" : "_p.Sync(_kind+\".cleanup\",_r,_id);")}}}
            }
            public sealed class KF<T>:{{role}}<Ping,K<T>>where T:class{
                private readonly Probe _p;public KF(Probe p){_p=p;}
                public {{(factoryAsync ? "async ValueTask<K<T>>" : "K<T>")}} {{create}}(Ping r,CancellationToken t){
                    var name={{KindExpression}};{{(factoryAsync ? "await _p.Void(name+\".factory\",r,0).ConfigureAwait(false);" : "_p.Sync(name+\".factory\",r,0);")}}
                    var value=new K<T>(_p,r);_p.Returned(value);return value;
                }
            }
            """;
        var handler = $$"""
            [ZendiatorHandler]public sealed class H:{{(async ? "IRequestHandler" : "ISyncRequestHandler")}}<Ping,int>,{{disposable}}{
                private readonly Probe _p;private readonly Ping _r;private readonly int _id;public H(Probe p,Ping r){_p=p;_r=r;_id=p.Create(this,"h",r);}
                public {{result}} {{(async ? "HandleAsync" : "Handle")}}(Ping r,CancellationToken t){AssertOwner(r);{{(async ? "if(_p.Mode==\"reentrant\"&&r.Depth==0)return Nested(r,t);return _p.Value(\"h.body\",r,_id,r.Value);" : "_p.Sync(\"h.body\",r,_id);return r.Value;")}}}
                private void AssertOwner(Ping r){_p.Check(_r.Stamp,r);}
                {{(async ? "private async ValueTask<int> Nested(Ping r,CancellationToken t){var own=await _p.Value(\"h.body\",r,_id,r.Value).ConfigureAwait(false);var child=await _p.Reenter!(r.Op+\"/child\").ConfigureAwait(false);return own+child;}" : "")}}
                public {{(async ? "ValueTask DisposeAsync()" : "void Dispose()")}}{_p.Release(this);{{(async ? "return _p.Void(\"h.cleanup\",_r,_id);" : "_p.Sync(\"h.cleanup\",_r,_id);")}}}
            }
            public sealed class HF:{{role}}<Ping,H>{
                private readonly Probe _p;public HF(Probe p){_p=p;}
                public {{(factoryAsync ? "async ValueTask<H>" : "H")}} {{create}}(Ping r,CancellationToken t){
                    {{(factoryAsync ? "await _p.Void(\"h.factory\",r,0).ConfigureAwait(false);" : "_p.Sync(\"h.factory\",r,0);")}}
                    var value=new H(_p,r);
                    {{(factoryAsync ? "if(_p.PartialFactory){try{throw _p.FactoryError;}catch{await value.DisposeAsync().ConfigureAwait(false);throw;}}" : "")}}
                    _p.Returned(value);return value;
                }
            }
            """;
        var wrapper = concurrent ? "public sealed class W:IAsyncSendWrapper<Ping,int>{public async ValueTask<int> InvokeAsync<TNext>(Ping r,TNext next,CancellationToken t)where TNext:IAsyncSendContinuation<Ping,int>{var a=next.InvokeAsync(r,t);var b=next.InvokeAsync(r,t);return await a.ConfigureAwait(false)+await b.ConfigureAwait(false);}}" : "";
        var pipeline = concurrent ? "Stage.Hook<K<OuterTag>,KF<OuterTag>>(),Stage.Wrapper<W>(),Stage.Hook<K<InnerTag>,KF<InnerTag>>()" : "Stage.Hook<K<RootTag>,KF<RootTag>>()";
        var source = $$"""
            using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.CrossCuttingProbe;
            namespace App{
                public sealed record Ping(string Op,int Value,int Stamp,int Depth):{{(async ? "IRequest" : "ISyncRequest")}}<int>,global::Zendiator.Generator.Tests.ICrossRequest;
                public sealed class RootTag;public sealed class OuterTag;public sealed class InnerTag;
                {{hook}}{{handler}}{{wrapper}}
                [ZendiatorComposition("crosscut")]public sealed class Reads;
                public static class Bridge{
                    public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{c.Handlers<Ping>(DispatchFamily.{{(async ? "SendAsync" : "SendSync")}},HandlerEntry.Session<H,HF>());c.Pipeline<Ping>(DispatchFamily.{{(async ? "SendAsync" : "SendSync")}},{{pipeline}});});
                    public static object Dispatch(object m,object r,CancellationToken t)=>((global::App.Generated.Reads.IZendiator)m).{{(async ? "SendAsync" : "Send")}}((Ping)r,t);
                }
            }
            """;
        return source + CrossReference(async, factoryAsync, concurrent);
    }
    // Handwritten native orchestration, independent from bound/emission models and output.
    private static string CrossReference(bool async, bool factoryAsync, bool concurrent)
    {
        var result = async ? "ValueTask<int>" : "int"; string Await(string value, bool a) => a ? "await " + value + ".ConfigureAwait(false)" : value;
        string Region(string method, string tag, bool wrapper)
        {
            var text = "private static " + (async ? "async " : "") + result + " " + method + "(IServiceProvider p,Ping r,CancellationToken t){t.ThrowIfCancellationRequested();K<" + tag + ">? k=" + Await("p.GetRequiredService<KF<" + tag + ">>()." + (factoryAsync ? "CreateAsync" : "Create") + "(r,t)", factoryAsync) + "??throw new InvalidOperationException();try{var saved=r;var ok=false;Exception? error=null;try{saved=" + Await("k." + (async ? "BeforeAsync" : "Before") + "(saved,t)", async) + ";ArgumentNullException.ThrowIfNull(saved);t.ThrowIfCancellationRequested();int value;";
            if (wrapper) text += "value=await p.GetRequiredService<W>().InvokeAsync(saved,new Next(p),t).ConfigureAwait(false);";
            else text += "H? h=" + Await("p.GetRequiredService<HF>()." + (factoryAsync ? "CreateAsync" : "Create") + "(saved,t)", factoryAsync) + "??throw new InvalidOperationException();try{value=" + Await("h." + (async ? "HandleAsync" : "Handle") + "(saved,t)", async) + ";}finally{var detached=h;h=null;" + Await("detached." + (async ? "DisposeAsync" : "Dispose") + "()", async) + ";}";
            return text + "value=" + Await("k." + (async ? "AfterAsync" : "After") + "(saved,value,t)", async) + ";ok=true;return value;}catch(Exception failure){error=failure;try{" + Await("k." + (async ? "OnExceptionAsync" : "OnException") + "(saved,failure,t)", async) + ";}catch(Exception replacement){error=replacement;throw;}throw;}finally{" + Await("k." + (async ? "FinallyAsync" : "Finally") + "(saved,new(ok,error),t)", async) + ";}}finally{var detached=k;k=null;" + Await("detached." + (async ? "DisposeAsync" : "Dispose") + "()", async) + ";}}";
        }
        var source = "namespace App{public static class Oracle{public static " + result + " Send(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);return Outer(p,r,t);}" + Region("Outer", concurrent ? "OuterTag" : "RootTag", concurrent);
        if (concurrent) source += "private readonly struct Next:IAsyncSendContinuation<Ping,int>{private readonly IServiceProvider _p;public Next(IServiceProvider p){_p=p;}public ValueTask<int> InvokeAsync(Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);return Inner(_p,r,t);}}" + Region("Inner", "InnerTag", false);
        return source + "}}";
    }
    private static CrossFixture CrossRuntimeFixture(string kind) => CrossFixtures.GetOrAdd(kind, k => new(() =>
    {
        var source = CrossSource(k); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "Cross_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); var sources = driver.GetRunResult().Results.Single().GeneratedSources; AssertCanonicalOutputRoles(sources);
        var e = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (!string.IsNullOrEmpty(e)) { var p = Path.Combine(e, "cross-" + k); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "input.cs"), source); foreach (var s in sources) File.WriteAllText(Path.Combine(p, s.HintName), s.SourceText.ToString()); File.WriteAllBytes(Path.Combine(p, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(p, "compile.json"), JsonSerializer.Serialize(new { emitted.Success, productionEnabled = true, candidateOnly = false, warnings = 0, errors = 0, generatorErrors = diagnostics.Length })); }
        bytes.Position = 0; var a = AssemblyLoadContext.Default.LoadFromStream(bytes); var b = a.GetType("App.Bridge")!; return new(a, a.GetType("App.Generated.Reads.Zendiator")!, a.GetType("App.Ping")!, b.GetMethod("Register")!, b.GetMethod("Dispatch")!, a.GetType("App.Oracle")!.GetMethod("Send")!);
    })).Value;
    private static object CrossRequest(CrossFixture f, string op, int value = 5, int depth = 0) => Activator.CreateInstance(f.Request, [op, value, 0, depth])!;
    private static object CrossInvoke(CrossFixture f, CrossCuttingProbe p, IServiceProvider provider, object mediator, object request, bool generated)
    { try { return generated ? f.Dispatch.Invoke(null, [mediator, request, p.Cancellation.Token])! : f.Reference.Invoke(null, [provider, request, p.Cancellation.Token])!; } catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; } }
    private static void CrossIdentity(CrossCuttingProbe p)
    {
        Assert.Equal(p.Resources.Count, p.Releases.Count); Assert.All(p.Releases, v => Assert.Equal(1, v.Value));
        foreach (var e in p.Events.Where(e => e.Phase.EndsWith(".after", StringComparison.Ordinal) || e.Phase.EndsWith(".finally", StringComparison.Ordinal) || e.Phase.EndsWith(".exception", StringComparison.Ordinal))) Assert.Equal(e.Receiver, e.Stamp);
        foreach (var h in p.Resources.Where(x => x.Value.Kind == "h")) { Assert.Contains(p.Resources.Values, k => k.Id == h.Value.ParentStamp && k.Kind != "h" && k.Op == h.Value.Op); Assert.Single(p.Events, e => e.Phase == "h.cleanup" && e.Receiver == h.Value.Id); }
    }
    private static void CrossEvidence(string scenario, bool generated, CrossCuttingProbe p, object? additional = null)
    { WriteNestedHookEvidence("cross-observations", scenario + "-" + generated, new { scenario, generated, events = p.Events.Select(e => new { e.Phase, e.Op, e.Stamp, e.Receiver, error = e.Error?.Message, success = e.Exit?.Success, exitError = e.Exit?.Error?.Message, e.Ambient, e.Culture, e.Context, e.Scheduler }), resources = p.Resources.Values.OrderBy(x => x.Id), releases = p.Releases.Count, consumptions = p.Gates.Values.Select(g => g.Consumptions), additional }); }
    [Theory]
    [InlineData("success")]
    [InlineData("exception")]
    [InlineData("oce")]
    public async Task Cross_cutting_consecutive_suspends_preserve_saved_request_unwind_status_and_single_consumption(string scenario)
    {
        var f = CrossRuntimeFixture("life"); foreach (var generated in new[] { false, true })
        {
            var p = new CrossCuttingProbe { Continuous = true }; var oce = new OperationCanceledException("foreign", p.Foreign.Token); if (scenario == "exception") p.Failures["h.body"] = p.BodyError; if (scenario == "oce") p.Failures["k.after"] = oce;
            try
            {
                IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Register.Invoke(null, [services]); await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope(); var mediator = scope.ServiceProvider.GetRequiredService(CanonicalEntryService(f.Mediator)); var request = CrossRequest(f, "chain"); var task = AsyncTask(CrossInvoke(f, p, scope.ServiceProvider, mediator, request, generated), false); Assert.False(task.IsCompleted); await p.Drain(task).WaitAsync(TimeSpan.FromSeconds(15)); Exception? error = null; try { await task; } catch (Exception e) { error = e; }
                if (scenario == "success") { Assert.Null(error); Assert.Equal(7, await (Task<int>)task); } else Assert.Same(scenario == "oce" ? oce : p.BodyError, error); Assert.Equal(scenario == "oce", task.IsCanceled); Assert.Equal(scenario == "exception", task.IsFaulted);
                var phases = p.Events.Select(e => e.Phase).ToArray(); Assert.Equal(scenario == "success" ? new[] { "k.factory", "k.before", "h.factory", "h.body", "h.cleanup", "k.after", "k.finally", "k.cleanup" } : scenario == "exception" ? new[] { "k.factory", "k.before", "h.factory", "h.body", "h.cleanup", "k.exception", "k.finally", "k.cleanup" } : new[] { "k.factory", "k.before", "h.factory", "h.body", "h.cleanup", "k.after", "k.exception", "k.finally", "k.cleanup" }, phases);
                Assert.All(p.Events.Where(e => e.Exit != null), e => { Assert.Equal(scenario == "success", e.Exit!.Value.Success); Assert.Same(error, e.Exit.Value.Error); }); CrossIdentity(p); CrossEvidence("chain-" + scenario, generated, p, new { task.IsCanceled, task.IsFaulted, result = scenario == "success" ? 7 : 0 });
            }
            finally { p.Cancellation.Dispose(); p.Foreign.Dispose(); }
        }
    }
    [Theory]
    [InlineData("parallel")]
    [InlineData("reentrant")]
    [InlineData("duplicate-next")]
    public async Task Cross_cutting_parallel_reentrant_and_duplicate_next_keep_invocation_and_receiver_identity_separate(string scenario)
    {
        var f = CrossRuntimeFixture(scenario == "duplicate-next" ? "concurrent" : "life"); foreach (var generated in new[] { false, true })
        {
            var p = new CrossCuttingProbe { Mode = scenario, PendingBody = scenario != "reentrant" };
            try
            {
                IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Register.Invoke(null, [services]); await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope(); var mediator = scope.ServiceProvider.GetRequiredService(CanonicalEntryService(f.Mediator));
                p.Reenter = op => new((Task<int>)AsyncTask(CrossInvoke(f, p, scope.ServiceProvider, mediator, CrossRequest(f, op, 30, 1), generated), false));
                var a = (Task<int>)AsyncTask(CrossInvoke(f, p, scope.ServiceProvider, mediator, CrossRequest(f, "A"), generated), false); Task<int>? b = null; if (scenario == "parallel") b = (Task<int>)AsyncTask(CrossInvoke(f, p, scope.ServiceProvider, mediator, CrossRequest(f, "B", 10), generated), false);
                if (p.PendingBody) { await p.WaitForBodies(2); Assert.False(a.IsCompleted); if (b != null) Assert.False(b.IsCompleted); p.CompleteBodies(); }
                await a.WaitAsync(TimeSpan.FromSeconds(10)); if (b != null) await b.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(scenario == "duplicate-next" ? 17 : scenario == "reentrant" ? 39 : 7, await a); if (b != null) Assert.Equal(12, await b); CrossIdentity(p); Assert.Equal(2, p.Resources.Values.Count(r => r.Kind == "h"));
                if (scenario == "parallel" || scenario == "reentrant") Assert.Equal(scenario == "parallel" ? new[] { "A", "B" } : new[] { "A", "A/child" }, p.Resources.Values.Where(r => r.Kind == "h").Select(r => r.Op).OrderBy(x => x)); else { var inner = p.Resources.Values.Where(r => r.Kind == "inner").ToArray(); Assert.Equal(2, inner.Length); Assert.Equal(2, inner.Select(r => r.Id).Distinct().Count()); Assert.Single(p.Resources.Values, r => r.Kind == "outer"); }
                Assert.All(p.Gates.Values, g => Assert.Equal(1, g.Consumptions)); CrossEvidence(scenario, generated, p, new { first = await a, second = b == null ? 0 : await b });
            }
            finally { p.Cancellation.Dispose(); p.Foreign.Dispose(); }
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cross_cutting_factory_unreturned_partial_resource_recovery_is_factory_owned_and_failure_masks_original(bool recoveryFails)
    {
        var f = CrossRuntimeFixture("life"); foreach (var generated in new[] { false, true })
        {
            var p = new CrossCuttingProbe { PartialFactory = true, RecoveryFails = recoveryFails }; if (recoveryFails) p.Failures["h.cleanup"] = p.RecoveryError;
            try
            {
                IServiceCollection services = new ServiceCollection(); services.AddSingleton(p); f.Register.Invoke(null, [services]); await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope(); var task = AsyncTask(CrossInvoke(f, p, scope.ServiceProvider, scope.ServiceProvider.GetRequiredService(CanonicalEntryService(f.Mediator)), CrossRequest(f, "partial"), generated), false); var error = await Assert.ThrowsAsync<Exception>(async () => await task); Assert.Same(recoveryFails ? p.RecoveryError : p.FactoryError, error); Assert.True(task.IsFaulted); Assert.False(task.IsCanceled);
                var h = Assert.Single(p.Resources.Values, r => r.Kind == "h"); Assert.False(h.Returned); Assert.DoesNotContain(p.Events, e => e.Phase == "h.body" || e.Phase == "k.after"); Assert.Single(p.Events, e => e.Phase == "h.cleanup"); Assert.Same(error, Assert.Single(p.Events, e => e.Phase == "k.exception").Error); CrossIdentity(p); CrossEvidence("partial-" + recoveryFails, generated, p, new { escaping = error.Message, factoryReturned = h.Returned });
            }
            finally { p.Cancellation.Dispose(); p.Foreign.Dispose(); }
        }
    }
}
