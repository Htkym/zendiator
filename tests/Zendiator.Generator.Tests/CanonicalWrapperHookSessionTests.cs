using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record WrapperHookFixture(Assembly Assembly,Type Mediator,Type Request,MethodInfo Register,MethodInfo Dispatch,MethodInfo Reference);
    private static readonly ConcurrentDictionary<(bool Async,bool Void,bool Two,string Profile),Lazy<WrapperHookFixture>> WrapperHookFixtures=new();
    private static (string W0,string W1,string Handler,string[] Modes) WrapperHookRoles(bool async,bool two,string profile)=>profile switch{
        "sync-response"=>("owned","direct","owned",["owned","borrowed","owned","direct"]),
        "sync-void"=>("direct","owned","borrowed",["borrowed","owned","borrowed","owned","owned"]),
        "async-void"=>("direct","direct","borrowed",["direct","async-borrowed","owned","async-borrowed"]),
        "async-two-response"=>("async-owned","borrowed","async-borrowed",["async-borrowed","owned","async-owned","direct","async-owned"]),
        "async-two-void"=>("borrowed","async-owned","async-owned",["async-owned","async-borrowed","direct","owned","async-borrowed"]),
        _=>(async?"async-owned":"owned",async?"async-owned":"owned",async?"async-owned":"owned",Enumerable.Repeat(async?"async-owned":"owned",two?5:4).ToArray())};
    private static string[] WrapperHookPatterns(bool async,bool two)=>async?(two?["s--s","--a-","saas","---a","-s-s"]:["s--s","--a-","saas","---a"]):(two?["s--s","--s-","ssss","---s","-s-s"]:["s--s","--s-","ssss","---s"]);
    private static string WrapperHookSource(bool async,bool isVoid,bool two,string profile)
    {
        var roles=WrapperHookRoles(async,two,profile);var source=NestedWrapperSource(async,isVoid,two,roles.W0,roles.W1,roles.Handler,false);source=source[..source.IndexOf("namespace App{public static class Oracle",StringComparison.Ordinal)];var patterns=WrapperHookPatterns(async,two);
        string Stage(int i)=>$"Stage.Hook<K{i}{(roles.Modes[i]=="direct"?"":",KF"+i)}>()";
        var stage0=$"Stage.Wrapper<W0{(roles.W0=="direct"?"":",WF0")}>()";source=source.Replace(stage0,Stage(0)+","+Stage(1)+","+stage0+","+Stage(2)+(two?"":","+Stage(3)));
        if(two){var stage1=$"Stage.Wrapper<W1{(roles.W1=="direct"?"":",WF1")}>()";source=source.Replace(stage1,stage1+","+Stage(3)+","+Stage(4));}
        for(var i=0;i<patterns.Length;i++){
            var contracts=new List<string>();var methods=new List<string>();var pattern=patterns[i];var id="k"+i;var mode=roles.Modes[i];
            for(var phase=0;phase<4;phase++)if(pattern[phase]!='-'){
                var a=pattern[phase]=='a';var prefix=a?"IAsyncSend":"ISend";contracts.Add(phase switch{0=>prefix+"RequestTransform<Ping>",1=>prefix+(isVoid?"SuccessObserver<Ping>":"ResponseTransform<Ping,int>"),2=>prefix+"ExceptionObserver<Ping>",_=>prefix+"FinallyObserver<Ping>"});
                methods.Add(phase switch{
                    0=>$"public {(a?"ValueTask<Ping>":"Ping")} {(a?"BeforeAsync":"Before")}(Ping r,CancellationToken t){{_p.Hit(\"{id}.before\",r,t);var value=_p.NullNext==\"{id}\"?null!:new Ping(r.Value+1);return {(a?"new(value)":"value")};}}",
                    1=>$"public {(a?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int"))} {(a?"AfterAsync":"After")}(Ping r,{(isVoid?"":"int value,")}CancellationToken t){{_p.Hit(\"{id}.after\",r,t);{(isVoid?(a?"return default;":""):(a?"return new(value+1);":"return value+1;"))}}}",
                    2=>$"public {(a?"ValueTask":"void")} {(a?"OnExceptionAsync":"OnException")}(Ping r,Exception e,CancellationToken t){{_p.Hit(\"{id}.exception\",r,t,e);{(a?"return default;":"")}}}",
                    _=>$"public {(a?"ValueTask":"void")} {(a?"FinallyAsync":"Finally")}(Ping r,SendExitInfo e,CancellationToken t){{_p.Hit(\"{id}.finally\",r,t,exit:e);{(a?"return default;":"")}}}"});
            }
            var dispose=async?$"public ValueTask DisposeAsync()=>_p.ReleaseAsync(\"{id}\",this);":$"public void Dispose()=>_p.Release(\"{id}\",this);";
            source+=$"namespace App{{public sealed class K{i}:{string.Join(',',contracts)},{(async?"IAsyncDisposable":"IDisposable")}{{private readonly Probe _p;public K{i}(Probe p){{_p=p;p.Construct(\"{id}\",this);if(p.CancelNext==\"{id}\")p.Token.Cancel();}}{string.Join(' ',methods)}{dispose}}}}}";
            if(mode!="direct"){var a=mode.StartsWith("async-",StringComparison.Ordinal);var role="I"+(a?"Async":"")+(mode.EndsWith("owned",StringComparison.Ordinal)?"Owned":"Borrowed")+"SessionFactory";source+=$"namespace App{{public sealed class KF{i}:{role}<Ping,K{i}>,IDisposable{{private readonly Probe _p;public KF{i}(Probe p){{_p=p;}}public {(a?"ValueTask<K"+i+">":"K"+i)} {(a?"CreateAsync":"Create")}(Ping r,CancellationToken t)=>_p.Create{(a?"Async":"")}(\"{id}\",r,t,()=>new K{i}(_p));public void Dispose()=>_p.DisposeFactory(\"{id}\");}}}}";}
        }
        return source+CanonicalWrapperHookSessionReference.Source(async,isVoid,two,roles.W0,roles.W1,roles.Handler,roles.Modes,patterns);
    }
    private static WrapperHookFixture WrapperHookRuntimeFixture(bool async,bool isVoid,bool two,string profile)=>WrapperHookFixtures.GetOrAdd((async,isVoid,two,profile),k=>new(()=>{
        var source=WrapperHookSource(k.Async,k.Void,k.Two,k.Profile);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"WrapperHooks_"+Guid.NewGuid().ToString("N")),out var output,out var diagnostics);Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var sources=driver.GetRunResult().Results.Single().GeneratedSources;Assert.Equal(2,sources.Length);
        var body=sources.Single(s=>s.HintName=="Zendiator.Canonical.Wrappers.g.cs").SourceText.ToString();var root=body[..body.IndexOf("private readonly struct WrapperNext0",StringComparison.Ordinal)];Assert.DoesNotContain("success1",root);Assert.DoesNotContain("error1",root);Assert.Equal(k.Async?(k.Two?3:2):0,body.Split("private async ",StringSplitOptions.None).Length-1);Assert.DoesNotContain("GetAwaiter",body);if(!k.Async)Assert.DoesNotContain("ValueTask",body);if(k.Void)Assert.DoesNotContain(" response;",body);
        var e=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");if(!string.IsNullOrEmpty(e)){var p=Path.Combine(e,"wrapper-hooks-"+k.Async+"-"+k.Void+"-"+k.Two+"-"+k.Profile);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);foreach(var s in sources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=diagnostics.Length}));}
        bytes.Position=0;var a=AssemblyLoadContext.Default.LoadFromStream(bytes);var b=a.GetType("App.Bridge")!;return new(a,a.GetType("App.Generated.Reads.Zendiator")!,a.GetType("App.Ping")!,b.GetMethod("Register")!,b.GetMethod("Dispatch")!,a.GetType("App.Oracle")!.GetMethod("Send")!);
    })).Value;
    [Theory][InlineData(false,false,false,"sync-response")][InlineData(false,true,true,"sync-void")]
    [InlineData(true,false,false,"owned")][InlineData(true,true,false,"async-void")][InlineData(true,false,true,"async-two-response")][InlineData(true,true,true,"async-two-void")]
    public async Task Wrapper_hooks_session_and_continuous_regions_reference_generation_standard_API_preserve_ownership(bool async,bool isVoid,bool two,string profile)
    {var f=WrapperHookRuntimeFixture(async,isVoid,two,profile);var expected=await RunWrapperHooks(f,async,isVoid,two,profile,false,"success");Assert.Equal(expected,await RunWrapperHooks(f,async,isVoid,two,profile,true,"success"));}
    [Theory][InlineData(false,"outer-skip")][InlineData(false,"inner-repeat")][InlineData(false,"middle-repeat")][InlineData(false,"second-hook-acquire")]
    [InlineData(true,"inner-hook-null")][InlineData(false,"after-failure")][InlineData(false,"observer-replace")][InlineData(false,"finally-replace")]
    [InlineData(false,"cleanup-priority")][InlineData(true,"pending-inner-acquire")][InlineData(false,"cancel-after-acquire")][InlineData(false,"null-transform")]
    public async Task Wrapper_hooks_distinct_acquire_unwind_next_and_callback_boundaries_preserve_D02_D04(bool isVoid,string scenario)
    {var two=scenario=="middle-repeat";var f=WrapperHookRuntimeFixture(true,isVoid,two,"owned");var expected=await RunWrapperHooks(f,true,isVoid,two,"owned",false,scenario);Assert.Equal(expected,await RunWrapperHooks(f,true,isVoid,two,"owned",true,scenario));}
    private static async Task<string> RunWrapperHooks(WrapperHookFixture f,bool async,bool isVoid,bool two,string profile,bool generated,string scenario)
    {
        var roles=WrapperHookRoles(async,two,profile);var p=new NestedWrapperProbe();Exception body=new("body"),acquire=new("acquire"),callback=new("callback"),cleanup=new("cleanup"),outer=new("outer");
        if(scenario=="outer-skip")p.Skip="w0";if(scenario is "inner-repeat" or "middle-repeat")p.Repeat="w0";if(scenario=="second-hook-acquire")p.Failures["k1.acquire"]=acquire;if(scenario=="inner-hook-null")p.NullReceiver="k2";
        if(scenario=="after-failure")p.Failures["k2.after"]=callback;if(scenario is "observer-replace" or "finally-replace" or "cleanup-priority")p.Failures["h.body"]=body;if(scenario=="observer-replace")p.Failures["k1.exception"]=callback;if(scenario=="finally-replace")p.Failures["k3.finally"]=callback;if(scenario=="cleanup-priority"){p.Failures["k3.cleanup"]=cleanup;p.Failures["k0.cleanup"]=outer;}
        if(scenario=="pending-inner-acquire"){p.Pending="k2.acquire";p.PendingError=acquire;}if(scenario=="cancel-after-acquire")p.CancelNext="k2";if(scenario=="null-transform")p.NullNext="k2";
        string Identity(Exception? e)=>e==null?"":ReferenceEquals(e,body)?"body":ReferenceEquals(e,acquire)?"acquire":ReferenceEquals(e,callback)?"callback":ReferenceEquals(e,cleanup)?"cleanup":ReferenceEquals(e,outer)?"outer":e.GetType().Name;
        try{IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);await using var provider=services.BuildServiceProvider();object? value=null;Exception? error=null;string snapshot;
            await using(var scope=provider.CreateAsyncScope()){
                var request=Activator.CreateInstance(f.Request,[10])!;object? Invoke(){try{return generated?f.Dispatch.Invoke(null,[scope.ServiceProvider.GetRequiredService(f.Mediator),request,p.Token.Token]):f.Reference.Invoke(null,[scope.ServiceProvider,request,p.Token.Token]);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
                if(async){var task=AsyncTask(Invoke()!,isVoid);if(p.Pending!=null){Assert.False(task.IsCompleted);Assert.NotNull(p.Gate);Assert.Equal(0,p.Gate.Consumptions);p.Gate.Fail(p.PendingError!);}try{await task.WaitAsync(TimeSpan.FromSeconds(10));if(!isVoid)value=await(Task<int>)task;}catch(Exception e){error=e;}Assert.Equal(error is OperationCanceledException,task.IsCanceled);Assert.Equal(error!=null&&error is not OperationCanceledException,task.IsFaulted);}else{try{value=Invoke();}catch(Exception e){error=e;}}
                if(p.Gate!=null)Assert.Equal(1,p.Gate.Consumptions);
                var expected=scenario switch{"second-hook-acquire" or "pending-inner-acquire"=>acquire,"after-failure" or "observer-replace" or "finally-replace"=>callback,"cleanup-priority"=>outer,_=>null};if(expected!=null)Assert.Same(expected,error);else if(scenario=="inner-hook-null")Assert.IsType<InvalidOperationException>(error);else if(scenario=="null-transform")Assert.IsType<ArgumentNullException>(error);else if(scenario=="cancel-after-acquire"){var oce=Assert.IsAssignableFrom<OperationCanceledException>(error);Assert.Equal(p.Token.Token,oce.CancellationToken);}else Assert.Null(error);
                string Mode(string id)=>id=="w0"?roles.W0:id=="w1"?roles.W1:id=="h"?roles.Handler:roles.Modes[int.Parse(id.AsSpan(1))];foreach(var r in p.Created)Assert.Equal(Mode(r.Value).EndsWith("owned",StringComparison.Ordinal),p.Released.Contains(r.Key));
                int Count(string id)=>p.Created.Count(x=>x.Value==id);if(scenario=="outer-skip"){Assert.Equal(0,Count("k2"));Assert.Equal(0,Count("h"));}if(scenario=="second-hook-acquire"){Assert.Equal(0,Count("k1"));Assert.Equal(0,Count("w0"));Assert.DoesNotContain(p.Calls,c=>c.Phase.StartsWith("k1.",StringComparison.Ordinal)&&c.Phase!="k1.acquire");}
                if(scenario is "inner-hook-null" or "pending-inner-acquire"){Assert.Equal(0,Count("k2"));Assert.Equal(0,Count("h"));}if(scenario is "cancel-after-acquire" or "null-transform"){Assert.Equal(1,Count("k2"));Assert.Equal(0,Count("k3"));Assert.Equal(0,Count("h"));}if(scenario=="inner-repeat"){Assert.Equal(1,Count("k0"));Assert.Equal(1,Count("k1"));Assert.Equal(2,Count("k2"));Assert.Equal(2,Count("k3"));Assert.Equal(2,Count("h"));}if(scenario=="middle-repeat"){Assert.Equal(2,Count("k2"));Assert.Equal(2,Count("k3"));Assert.Equal(2,Count("k4"));Assert.Equal(2,Count("h"));}
                if(scenario=="cleanup-priority"){Assert.Same(body,p.Calls.Single(c=>c.Phase=="k3.finally").Exit!.Value.Error);Assert.Same(cleanup,p.Calls.Single(c=>c.Phase=="k2.exception").Error);Assert.Same(cleanup,p.Calls.Single(c=>c.Phase=="k0.finally").Exit!.Value.Error);}
                foreach(var c in p.Calls.Where(c=>c.Phase.EndsWith(".finally",StringComparison.Ordinal))){Assert.Equal(c.Exit!.Value.Error==null,c.Exit.Value.Success);var id=c.Phase[..c.Phase.IndexOf('.')];var final=p.Trace.IndexOf(c.Phase);var dispose=p.Trace.IndexOf(id+".cleanup");if(dispose>=0)Assert.True(final<dispose);}
                if(scenario=="null-transform")Assert.All(p.Calls.Where(c=>c.Phase is "k2.exception" or "k2.finally"),c=>Assert.Null(c.Request));
                var observation=new{async,isVoid,two,profile,scenario,value,error=Identity(error),trace=p.Trace.ToArray(),calls=p.Calls.Select(c=>new{c.Phase,value=c.Request==null?null:f.Request.GetProperty("Value")!.GetValue(c.Request),error=Identity(c.Error),exit=Identity(c.Exit?.Error),success=c.Exit?.Success}),created=p.Created.Values.GroupBy(x=>x).ToDictionary(x=>x.Key,x=>x.Count()),released=p.Released.Select(x=>p.Created[x]).GroupBy(x=>x).ToDictionary(x=>x.Key,x=>x.Count()),consumptions=p.Gate?.Consumptions};snapshot=JsonSerializer.Serialize(observation);WriteNestedHookEvidence("wrapper-hook-observations",async+"-"+isVoid+"-"+two+"-"+profile+"-"+generated+"-"+scenario,new{generated,observation});p.Failures.Clear();p.Pending=null;
            }
            foreach(var r in p.Created.Keys.Where(x=>!p.Released.Contains(x)).ToArray()){if(async)await((IAsyncDisposable)r).DisposeAsync();else((IDisposable)r).Dispose();}Assert.Equal(p.Created.Count,p.Released.Count);Assert.All(p.FactoryDisposals,x=>Assert.Equal(1,x.Value));WriteNestedHookEvidence("wrapper-hook-lifetime",async+"-"+isVoid+"-"+two+"-"+profile+"-"+generated+"-"+scenario,new{generated,scenario,created=p.Created.Count,released=p.Released.Count,p.FactoryDisposals,p.Trace});return snapshot;
        }finally{p.Token.Dispose();p.Foreign.Dispose();}
    }
    [Fact]
    public void Wrapper_hooks_preserve_accepted_nested_and_single_DI_Wrapper_and_partial_session_generation_only()
    {NestedFixture(true,false,true,"async-owned","owned","async-owned",true);WrapperRuntimeFixture(true,false,"async-owned","both");IntegrationFixture(true,false,"saas","async-owned","async-owned",false);}
}
