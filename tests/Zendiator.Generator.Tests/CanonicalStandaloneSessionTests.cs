using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed class MetadataBorrowedReceiverFactory<R,H>:IBorrowedSessionFactory<R,H> where R:class where H:class
{public H Create(R r,CancellationToken t)=>throw new InvalidOperationException("Metadata fixture only.");}

public sealed partial class GeneratorTests
{
    private sealed record StandaloneSessionFixture(Assembly Assembly,Type Mediator,Type Request,MethodInfo Register,MethodInfo Dispatch,MethodInfo Reference);
    private static readonly ConcurrentDictionary<(bool Async,bool Void,string Profile,bool Hierarchy),Lazy<StandaloneSessionFixture>> StandaloneFixtures=new();
    private static string StandaloneSessionSource(bool async,bool isVoid,string profile,bool hierarchy)
    {
        var roles=WrapperHookRoles(async,false,profile);var patterns=WrapperHookPatterns(async,false);
        var source=WrapperHookSource(async,isVoid,false,profile);source=source[..source.IndexOf("namespace App{public static class Oracle",StringComparison.Ordinal)];
        var wrapper="Stage.Wrapper<W0"+(roles.W0=="direct"?"":",WF0")+">(),";Assert.Contains(wrapper,source);source=source.Replace(wrapper,"");
        if(hierarchy){
            source=source.Replace("public sealed class K0:","public class K0Base<T>:").Replace("public K0(Probe p)","public K0Base(Probe p)");
            source=source.Replace("public sealed class KF0:","public class KF0Base<T>:").Replace("public KF0(Probe p)","public KF0Base(Probe p)");
            source=source.Replace("[ZendiatorHandler]public sealed class Handler:","public class HandlerBase<T>:").Replace("public Handler(Probe p)","public HandlerBase(Probe p)");
            var signature=async?"public ValueTask OnExceptionAsync(Ping r,Exception e,CancellationToken t){_p.Hit(\"k1.exception\",r,t,e);return default;}":"public void OnException(Ping r,Exception e,CancellationToken t){_p.Hit(\"k1.exception\",r,t,e);}";
            var explicitSignature=signature.Replace("public ValueTask OnExceptionAsync","ValueTask IAsyncSendExceptionObserver<Ping>.OnExceptionAsync").Replace("public void OnException","void ISendExceptionObserver<Ping>.OnException");Assert.Contains(signature,source);source=source.Replace(signature,explicitSignature);
            var request="public sealed record Ping(int Value):"+(async?"IRequest":"ISyncRequest")+(isVoid?"":"<int>")+";";Assert.Contains(request,source);source=source.Replace(request,"");
            source=source.Insert(source.IndexOf("namespace App{",StringComparison.Ordinal),"using Ping=global::App.Box<string>.Ping;using KF0=global::App.Factories<string>.Inner;");
            source+="namespace App{public static class Box<T>where T:class{"+request+"}public sealed class K0:K0Base<string>{public K0(Probe p):base(p){}}public static class Factories<T>where T:class{public sealed class Inner:KF0Base<T>{public Inner(Probe p):base(p){}}}[ZendiatorHandler]public sealed partial class Handler:HandlerBase<string>{public Handler(Probe p):base(p){}}public sealed partial class Handler{}}";
        }
        return source+CanonicalWrapperHookSessionReference.Source(async,isVoid,false,roles.W0,roles.W1,roles.Handler,roles.Modes,patterns,standalone:true,explicitObserver:hierarchy);
    }
    private static StandaloneSessionFixture StandaloneFixture(bool async,bool isVoid,string profile,bool hierarchy)=>StandaloneFixtures.GetOrAdd((async,isVoid,profile,hierarchy),k=>new(()=>{
        var source=StandaloneSessionSource(k.Async,k.Void,k.Profile,k.Hierarchy);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"Standalone_"+Guid.NewGuid().ToString("N")),out var output,out var diagnostics);Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var sources=driver.GetRunResult().Results.Single().GeneratedSources;Assert.Equal(2,sources.Count(s=>s.HintName!="Zendiator.GeneratedCatalog.g.cs"));
        var body=sources.Single(s=>s.HintName=="Zendiator.Canonical.Capabilities.g.cs").SourceText.ToString();Assert.Equal(k.Async?1:0,body.Split("private async ",StringSplitOptions.None).Length-1);Assert.DoesNotContain("success1",body);Assert.DoesNotContain("error1",body);Assert.DoesNotContain("WrapperNext",body);Assert.DoesNotContain("GetAwaiter",body);if(!k.Async)Assert.DoesNotContain("ValueTask",body);if(k.Void)Assert.DoesNotContain(" response;",body);if(k.Hierarchy){Assert.Contains("global::App.Factories<string>.Inner",body);Assert.Contains("global::App.Box<string>.Ping",body);}
        var e=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");if(!string.IsNullOrEmpty(e)){var p=Path.Combine(e,"standalone-"+k.Async+"-"+k.Void+"-"+k.Profile+"-"+k.Hierarchy);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);foreach(var s in sources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=diagnostics.Length}));}
        bytes.Position=0;var a=AssemblyLoadContext.Default.LoadFromStream(bytes);var b=a.GetType("App.Bridge")!;var request=k.Hierarchy?a.GetType("App.Box`1+Ping")!.MakeGenericType(typeof(string)):a.GetType("App.Ping")!;return new(a,a.GetType("App.Generated.Reads.Zendiator")!,request,b.GetMethod("Register")!,b.GetMethod("Dispatch")!,a.GetType("App.Oracle")!.GetMethod("Send")!);
    })).Value;
    [Theory][InlineData(false,false,"sync-response",false)][InlineData(false,true,"owned",false)]
    [InlineData(true,false,"owned",false)][InlineData(true,true,"async-void",false)][InlineData(false,false,"sync-response",true)][InlineData(true,true,"owned",true)]
    public async Task Standalone_sessions_public_closed_source_shapes_and_hierarchy_match_reference_and_standard_API(bool async,bool isVoid,string profile,bool hierarchy)
    {var f=StandaloneFixture(async,isVoid,profile,hierarchy);var expected=await RunStandalone(f,async,isVoid,profile,hierarchy,false,"success");Assert.Equal(expected,await RunStandalone(f,async,isVoid,profile,hierarchy,true,"success"));}
    [Theory][InlineData(true,true,"body")][InlineData(false,false,"second-acquire")][InlineData(false,false,"cleanup-priority")][InlineData(false,false,"pending-acquire")][InlineData(false,false,"null-transform")]
    public async Task Standalone_sessions_structurally_distinct_failure_and_pending_cases_preserve_native_unwind(bool isVoid,bool hierarchy,string scenario)
    {var f=StandaloneFixture(true,isVoid,"owned",hierarchy);var expected=await RunStandalone(f,true,isVoid,"owned",hierarchy,false,scenario);Assert.Equal(expected,await RunStandalone(f,true,isVoid,"owned",hierarchy,true,scenario));}
    private static async Task<string> RunStandalone(StandaloneSessionFixture f,bool async,bool isVoid,string profile,bool hierarchy,bool generated,string scenario)
    {
        var roles=WrapperHookRoles(async,false,profile);var p=new NestedWrapperProbe();Exception body=new("body"),acquire=new("acquire"),inner=new("inner"),outer=new("outer");
        if(scenario is "body" or "cleanup-priority")p.Failures["h.body"]=body;if(scenario=="second-acquire")p.Failures["k1.acquire"]=acquire;if(scenario=="cleanup-priority"){p.Failures["k3.cleanup"]=inner;p.Failures["k0.cleanup"]=outer;}if(scenario=="pending-acquire"){p.Pending="k2.acquire";p.PendingError=acquire;}if(scenario=="null-transform")p.NullNext="k2";
        string Identity(Exception? e)=>e==null?"":ReferenceEquals(e,body)?"body":ReferenceEquals(e,acquire)?"acquire":ReferenceEquals(e,inner)?"inner":ReferenceEquals(e,outer)?"outer":e.GetType().Name;
        try{IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);await using var provider=services.BuildServiceProvider();object? value=null;Exception? error=null;string snapshot;
            await using(var scope=provider.CreateAsyncScope()){
                var request=Activator.CreateInstance(f.Request,[10])!;object? Invoke(){try{return generated?f.Dispatch.Invoke(null,[scope.ServiceProvider.GetRequiredService(f.Mediator),request,p.Token.Token]):f.Reference.Invoke(null,[scope.ServiceProvider,request,p.Token.Token]);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
                if(async){var task=AsyncTask(Invoke()!,isVoid);if(p.Pending!=null){Assert.False(task.IsCompleted);Assert.NotNull(p.Gate);Assert.Equal(0,p.Gate.Consumptions);p.Gate.Fail(p.PendingError!);}try{await task.WaitAsync(TimeSpan.FromSeconds(10));if(!isVoid)value=await(Task<int>)task;}catch(Exception e){error=e;}Assert.Equal(error is OperationCanceledException,task.IsCanceled);Assert.Equal(error!=null&&error is not OperationCanceledException,task.IsFaulted);}else{try{value=Invoke();}catch(Exception e){error=e;}}
                if(p.Gate!=null)Assert.Equal(1,p.Gate.Consumptions);var expected=scenario switch{"body"=>body,"second-acquire" or "pending-acquire"=>acquire,"cleanup-priority"=>outer,_=>null};if(expected!=null)Assert.Same(expected,error);else if(scenario=="null-transform")Assert.IsType<ArgumentNullException>(error);else Assert.Null(error);
                Assert.DoesNotContain(p.Created.Values,id=>id.StartsWith("w",StringComparison.Ordinal));Assert.All(p.Calls,c=>Assert.Equal(p.Token.Token,c.Token));
                foreach(var r in p.Created){var mode=r.Value=="h"?roles.Handler:roles.Modes[int.Parse(r.Value.AsSpan(1))];Assert.Equal(mode.EndsWith("owned",StringComparison.Ordinal),p.Released.Contains(r.Key));}
                int Count(string id)=>p.Created.Count(x=>x.Value==id);if(scenario=="second-acquire"){Assert.Equal(0,Count("k1"));Assert.Equal(0,Count("k2"));Assert.Equal(0,Count("h"));Assert.DoesNotContain(p.Calls,c=>c.Phase.StartsWith("k1.",StringComparison.Ordinal)&&c.Phase!="k1.acquire");}if(scenario=="pending-acquire"){Assert.Equal(0,Count("k2"));Assert.Equal(0,Count("h"));}if(scenario=="null-transform"){Assert.Equal(0,Count("k3"));Assert.Equal(0,Count("h"));Assert.All(p.Calls.Where(c=>c.Phase is "k2.exception" or "k2.finally"),c=>Assert.Null(c.Request));}
                if(scenario=="cleanup-priority"){Assert.Same(body,p.Calls.Single(c=>c.Phase=="k3.finally").Exit!.Value.Error);Assert.Same(inner,p.Calls.Single(c=>c.Phase=="k1.exception").Error);Assert.Same(inner,p.Calls.Single(c=>c.Phase=="k0.finally").Exit!.Value.Error);}if(hierarchy&&scenario=="body")Assert.Same(body,p.Calls.Single(c=>c.Phase=="k1.exception").Error);
                foreach(var c in p.Calls.Where(c=>c.Phase.EndsWith(".finally",StringComparison.Ordinal))){Assert.Equal(c.Exit!.Value.Error==null,c.Exit.Value.Success);var id=c.Phase[..c.Phase.IndexOf('.')];var cleanup=p.Trace.IndexOf(id+".cleanup");if(cleanup>=0)Assert.True(p.Trace.IndexOf(c.Phase)<cleanup);}
                var observation=new{async,isVoid,profile,hierarchy,scenario,value,error=Identity(error),trace=p.Trace.ToArray(),calls=p.Calls.Select(c=>new{c.Phase,value=c.Request==null?null:f.Request.GetProperty("Value")!.GetValue(c.Request),error=Identity(c.Error),exit=Identity(c.Exit?.Error),success=c.Exit?.Success}),created=p.Created.Values.GroupBy(x=>x).ToDictionary(x=>x.Key,x=>x.Count()),released=p.Released.Select(x=>p.Created[x]).GroupBy(x=>x).ToDictionary(x=>x.Key,x=>x.Count()),consumptions=p.Gate?.Consumptions};snapshot=JsonSerializer.Serialize(observation);WriteNestedHookEvidence("standalone-observations",async+"-"+isVoid+"-"+profile+"-"+hierarchy+"-"+generated+"-"+scenario,new{generated,observation});p.Failures.Clear();p.Pending=null;
            }
            foreach(var r in p.Created.Keys.Where(x=>!p.Released.Contains(x)).ToArray()){if(async)await((IAsyncDisposable)r).DisposeAsync();else((IDisposable)r).Dispose();}Assert.Equal(p.Created.Count,p.Released.Count);Assert.All(p.FactoryDisposals,x=>Assert.Equal(1,x.Value));WriteNestedHookEvidence("standalone-lifetime",async+"-"+isVoid+"-"+profile+"-"+hierarchy+"-"+generated+"-"+scenario,new{generated,scenario,created=p.Created.Count,released=p.Released.Count,p.FactoryDisposals,p.Trace});return snapshot;
        }finally{p.Token.Dispose();p.Foreign.Dispose();}
    }
    [Fact]
    public void Standalone_sessions_preserve_accepted_Wrapper_regions_and_single_session_and_zero_Hook_generation_only()
    {WrapperHookRuntimeFixture(true,false,false,"owned");IntegrationFixture(true,false,"saas","async-owned","async-owned",false);AsyncRuntimeFixture("async-owned",false);}
}
