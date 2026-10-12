using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public class CapabilityProbe
{
    public readonly List<string> Trace=[];
    public readonly List<(string Phase,object? Request,CancellationToken Token,Exception? Error,SendExitInfo? Exit)> Calls=[];
    public readonly Dictionary<string,Exception> Failures=[];
    public readonly List<IAsyncOnceGate> Gates=[];
    public readonly CancellationTokenSource Cancellation=new();
    public string? Pending,CancelPhase;public bool NullBefore;public int Disposals;
    protected void Hit(string phase,object? r,CancellationToken t,Exception? error=null,SendExitInfo? exit=null)
    {Trace.Add(phase);Calls.Add((phase,r,t,error,exit));if(CancelPhase==phase)Cancellation.Cancel();if(Failures.TryGetValue(phase,out var e))throw e;}
    public R Before<R>(int id,R r,R next,CancellationToken t)where R:class{Hit($"h{id}.before",r,t);return NullBefore?null!:next;}
    public ValueTask<R> BeforeAsync<R>(int id,R r,R next,CancellationToken t)where R:class=>Step($"h{id}.before",r,t,NullBefore?null!:next);
    public int After(int id,object? r,int value,CancellationToken t){Hit($"h{id}.after",r,t);return value+1;}
    public ValueTask<int> AfterAsync(int id,object? r,int value,CancellationToken t)=>Step($"h{id}.after",r,t,value+1);
    public void AfterVoid(int id,object? r,CancellationToken t)=>Hit($"h{id}.after",r,t);
    public ValueTask AfterVoidAsync(int id,object? r,CancellationToken t)=>StepVoid($"h{id}.after",r,t);
    public void Observe(int id,object? r,Exception e,CancellationToken t)=>Hit($"h{id}.exception",r,t,e);
    public ValueTask ObserveAsync(int id,object? r,Exception e,CancellationToken t)=>StepVoid($"h{id}.exception",r,t,e);
    public void Finally(int id,object? r,SendExitInfo exit,CancellationToken t)=>Hit($"h{id}.finally",r,t,exit:exit);
    public ValueTask FinallyAsync(int id,object? r,SendExitInfo exit,CancellationToken t)=>StepVoid($"h{id}.finally",r,t,exit:exit);
    public int Handle(object r,int value,CancellationToken t){Hit("handler",r,t);return value;}
    public void HandleVoid(object r,CancellationToken t)=>Hit("handler",r,t);
    public ValueTask<int> HandleAsync(object r,int value,CancellationToken t)=>Step("handler",r,t,value);
    public ValueTask HandleVoidAsync(object r,CancellationToken t)=>StepVoid("handler",r,t);
    private ValueTask<T> Step<T>(string phase,object? r,CancellationToken t,T value,Exception? error=null,SendExitInfo? exit=null)
    {Hit(phase,r,t,error,exit);if(Pending!=phase)return new(value);var g=new AsyncOnceGate<T>(value);Gates.Add(g);return g.Task;}
    private ValueTask StepVoid(string phase,object? r,CancellationToken t,Exception? error=null,SendExitInfo? exit=null)
    {Hit(phase,r,t,error,exit);if(Pending!=phase)return default;var g=new AsyncOnceGate<int>(0);Gates.Add(g);return g.Void;}
    public void Dispose()=>Disposals++;
}

public sealed partial class GeneratorTests
{
    private sealed record CapabilityFixture(Assembly Assembly,Type Mediator,Type Request,MethodInfo Register,MethodInfo Dispatch,MethodInfo Reference);
    private static readonly ConcurrentDictionary<(bool Async,bool Void,string Pattern,bool Two),Lazy<CapabilityFixture>> CapabilityFixtures=new();
    private static string CapabilitySource(bool async,bool isVoid,string pattern,bool two)
    {
        string Hook(int id)
        {
            var contracts=new List<string>();var methods=new List<string>();
            foreach(var phase in Enumerable.Range(0,4))
            {
                var kind=pattern[phase];if(kind=='-')continue;var a=kind=='a';var prefix=a?"IAsyncSend":"ISend";
                var contract=phase switch{0=>prefix+"RequestTransform<Ping>",1=>prefix+(isVoid?"SuccessObserver<Ping>":"ResponseTransform<Ping,int>"),2=>prefix+"ExceptionObserver<Ping>",_=>prefix+"FinallyObserver<Ping>"};contracts.Add(contract);
                methods.Add(phase switch{
                    0=>$"public {(a?"ValueTask<Ping>":"Ping")} {(a?"BeforeAsync":"Before")}(Ping r,CancellationToken t)=>_p.{(a?"BeforeAsync":"Before")}({id},r,new Ping(r.Value+1),t);",
                    1=>$"public {(a?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int"))} {(a?"AfterAsync":"After")}(Ping r,{(isVoid?"":"int value,")}CancellationToken t)=>_p.{(isVoid?(a?"AfterVoidAsync":"AfterVoid"):(a?"AfterAsync":"After"))}({id},r,{(isVoid?"":"value,")}t);",
                    2=>$"public {(a?"ValueTask":"void")} {(a?"OnExceptionAsync":"OnException")}(Ping r,Exception e,CancellationToken t)=>_p.{(a?"ObserveAsync":"Observe")}({id},r,e,t);",
                    _=>$"public {(a?"ValueTask":"void")} {(a?"FinallyAsync":"Finally")}(Ping r,SendExitInfo e,CancellationToken t)=>_p.{(a?"FinallyAsync":"Finally")}({id},r,e,t);"});
            }
            return $"public sealed class Hook{id}:{string.Join(',',contracts)}{{private readonly Probe _p;public Hook{id}(Probe p){{_p=p;}}{string.Join(' ',methods)}}}";
        }
        var source=$$"""
            using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Probe=global::Zendiator.Generator.Tests.CapabilityProbe;
            namespace App{
                public sealed record Ping(int Value):{{(async?"IRequest":"ISyncRequest")}}{{(isVoid?"":"<int>")}};
                {{Hook(0)}}{{Hook(1)}}
                [ZendiatorHandler]public sealed class Handler:{{(async?"IRequestHandler":"ISyncRequestHandler")}}<Ping{{(isVoid?"":",int")}}>,IDisposable{
                    private readonly Probe _p;public Handler(Probe p){_p=p;}
                    public {{(async?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int"))}} {{(async?"HandleAsync":"Handle")}}(Ping r,CancellationToken t)=>_p.{{(isVoid?(async?"HandleVoidAsync":"HandleVoid"):(async?"HandleAsync":"Handle"))}}(r,{{(isVoid?"":"r.Value,")}}t);
                    public void Dispose()=>_p.Dispose();
                }
                [ZendiatorComposition("capabilities")]public sealed class Reads;
                public static class Bridge{
                    public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{c.Handlers<Ping>(DispatchFamily.{{(async?"SendAsync":"SendSync")}},typeof(Handler));c.Pipeline<Ping>(DispatchFamily.{{(async?"SendAsync":"SendSync")}},Stage.Hook<Hook0>(){{(two?",Stage.Hook<Hook1>()":"")}});});
                    public static object? Dispatch(object m,object? r,CancellationToken t){ {{(async||!isVoid?"return ":"")}}((global::App.Generated.Reads.IZendiator)m).{{(async?"SendAsync":"Send")}}((Ping)r!,t);{{(!async&&isVoid?"return null;":"")}} }
                }
            }
            """;
        return source+CanonicalCapabilityReference.Source(async,isVoid,pattern[0].ToString(),pattern[1].ToString(),pattern[2].ToString(),pattern[3].ToString(),two);
    }
    private static CapabilityFixture CapabilityRuntimeFixture(bool async,bool isVoid,string pattern,bool two)=>CapabilityFixtures.GetOrAdd((async,isVoid,pattern,two),k=>new(()=>
    {
        var source=CapabilitySource(k.Async,k.Void,k.Pattern,k.Two);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"Capability_"+Guid.NewGuid().ToString("N")),out var output,out var diagnostics);Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var sources=driver.GetRunResult().Results.Single().GeneratedSources;Assert.Equal(2,sources.Length);
        var e=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");if(!string.IsNullOrEmpty(e)){var p=Path.Combine(e,"capability-"+k.Async+"-"+k.Void+"-"+k.Pattern+"-"+k.Two);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);foreach(var s in sources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=diagnostics.Length}));}
        bytes.Position=0;var a=AssemblyLoadContext.Default.LoadFromStream(bytes);var b=a.GetType("App.Bridge")!;return new(a,a.GetType("App.Generated.Reads.Zendiator")!,a.GetType("App.Ping")!,b.GetMethod("Register")!,b.GetMethod("Dispatch")!,a.GetType("App.Oracle")!.GetMethod("Send")!);
    })).Value;
    public static IEnumerable<object[]> CapabilityShapes()
    {
        foreach(var a in new[]{false,true})foreach(var v in new[]{false,true})foreach(var pattern in(a?new[]{"s---","-a--","--s-","---a","saas","as-a","ssss","--as"}:new[]{"s---","-s--","--s-","---s"}))yield return[a,v,pattern,false];
        foreach(var v in new[]{false,true}){yield return[true,v,"saas",true];yield return[false,v,"---s",true];}
    }
    [Theory][MemberData(nameof(CapabilityShapes))]
    public async Task Send_capabilities_optional_and_mixed_completion_preserve_entered_guard_order_and_unwind(bool async,bool isVoid,string pattern,bool two)
    {var f=CapabilityRuntimeFixture(async,isVoid,pattern,two);foreach(var generated in new[]{false,true})foreach(var fail in new[]{false,true})await RunCapability(f,async,isVoid,pattern,two,generated,fail?"body":"success");}
    [Theory][InlineData(false,"before")][InlineData(true,"observer")][InlineData(false,"finally")][InlineData(true,"cancel")][InlineData(false,"null")]
    public async Task Send_capabilities_missing_observer_or_async_phase_keeps_exception_identity_and_canceled_status(bool isVoid,string scenario)
    {var pattern=scenario=="before"?"a--s":scenario=="observer"?"--a-":scenario=="finally"?"---a":"a--s";var f=CapabilityRuntimeFixture(true,isVoid,pattern,false);foreach(var generated in new[]{false,true})await RunCapability(f,true,isVoid,pattern,false,generated,scenario);}
    [Theory][InlineData(false,"h0.before")][InlineData(true,"h0.after")][InlineData(false,"h0.exception")][InlineData(true,"h0.finally")]
    public async Task Send_capabilities_selected_async_callback_really_suspends_once_with_synchronous_neighbors(bool isVoid,string phase)
    {var pattern=phase=="h0.before"?"as-s":phase=="h0.after"?"sass":phase=="h0.exception"?"ssas":"sssa";var f=CapabilityRuntimeFixture(true,isVoid,pattern,false);foreach(var generated in new[]{false,true})await RunCapability(f,true,isVoid,pattern,false,generated,phase=="h0.exception"?"body":"success",phase);}

    private static async Task RunCapability(CapabilityFixture f,bool async,bool isVoid,string pattern,bool two,bool generated,string scenario,string? pending=null)
    {
        var p=new CapabilityProbe{Pending=pending};var body=new Exception("body");var boundary=new Exception("boundary");using var foreign=new CancellationTokenSource();var oce=new OperationCanceledException("identity",foreign.Token);
        if(scenario is "body" or "observer" or "finally")p.Failures["handler"]=body;
        if(scenario=="before")p.Failures["h0.before"]=oce;if(scenario=="observer")p.Failures["h0.exception"]=boundary;if(scenario=="finally")p.Failures["h0.finally"]=boundary;p.CancelPhase=scenario=="cancel"?"h0.before":null;p.NullBefore=scenario=="null";
        try{
            IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);using var provider=services.BuildServiceProvider();object r=Activator.CreateInstance(f.Request,[10])!;Exception? error=null;object? result=null;
            using(var scope=provider.CreateScope()){
                object? Invoke(object? request){try{return generated?f.Dispatch.Invoke(null,[scope.ServiceProvider.GetRequiredService(f.Mediator),request,p.Cancellation.Token]):f.Reference.Invoke(null,[scope.ServiceProvider,request,p.Cancellation.Token]);}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
                Assert.Throws<ArgumentNullException>(()=>Invoke(null));Assert.Empty(p.Trace);
                if(async){var task=AsyncTask(Invoke(r)!,isVoid);if(pending!=null){Assert.False(task.IsCompleted);var g=Assert.Single(p.Gates);Assert.Equal(0,g.Consumptions);g.Complete();}try{await task.WaitAsync(TimeSpan.FromSeconds(10));if(!isVoid)result=await (Task<int>)task;}catch(Exception e){error=e;}Assert.Equal(error is OperationCanceledException,task.IsCanceled);Assert.Equal(error!=null&&error is not OperationCanceledException,task.IsFaulted);}else{try{result=Invoke(r);}catch(Exception e){error=e;}}
                var expected=scenario=="before"?oce:scenario is "observer" or "finally"?boundary:scenario=="body"?body:null;
                if(scenario=="cancel"){Assert.IsAssignableFrom<OperationCanceledException>(error);Assert.Equal(p.Cancellation.Token,((OperationCanceledException)error!).CancellationToken);}else if(scenario=="null")Assert.IsType<ArgumentNullException>(error);else Assert.Same(expected,error);
                if(error==null&&!isVoid)Assert.Equal(10+(pattern[0]!='-'?(two?2:1):0)+(pattern[1]!='-'?(two?2:1):0),result);
                var golden=new List<string>();void Expected(int index){var prefix=$"h{index}";if(pattern[0]!='-')golden.Add(prefix+".before");var failsBefore=index==0&&scenario is "before" or "cancel" or "null";
                    if(!failsBefore){if(two&&index==0)Expected(1);else golden.Add("handler");if(scenario=="success"&&pattern[1]!='-')golden.Add(prefix+".after");}
                    if(scenario!="success"&&pattern[2]!='-')golden.Add(prefix+".exception");if(pattern[3]!='-')golden.Add(prefix+".finally");}
                Expected(0);Assert.Equal(golden,p.Trace);Assert.All(p.Calls,c=>Assert.Equal(p.Cancellation.Token,c.Token));Assert.All(p.Gates,g=>Assert.Equal(1,g.Consumptions));
                foreach(var c in p.Calls.Where(c=>c.Exit!=null)){Assert.Equal(scenario=="success",c.Exit!.Value.Success);if(scenario=="observer")Assert.Same(boundary,c.Exit.Value.Error);else if(scenario=="body"||scenario=="finally")Assert.Same(body,c.Exit.Value.Error);}
                Assert.Equal(0,p.Disposals);WriteNestedHookEvidence("capability-observations",async+"-"+isVoid+"-"+pattern+"-"+two+"-"+generated+"-"+scenario+"-"+(pending??"complete"),new{async,isVoid,pattern,two,generated,scenario,pending,trace=p.Trace.ToArray(),result,error=error?.Message,consumptions=p.Gates.Select(g=>g.Consumptions),calls=p.Calls.Select(c=>new{c.Phase,value=c.Request?.GetType().GetProperty("Value")!.GetValue(c.Request),error=c.Error?.Message,success=c.Exit?.Success,exitError=c.Exit?.Error?.Message})});p.Failures.Clear();p.Pending=null;
            }
            Assert.Equal(p.Trace.Contains("handler")?1:0,p.Disposals);
        }finally{p.Cancellation.Dispose();}
    }
    [Theory][InlineData("ambiguous","ZEN0027")][InlineData("async-on-sync","ZEN0030")][InlineData("metadata-session","ZEN0037")]
    public void Send_capabilities_unproved_or_incompatible_contracts_remain_diagnosed(string shape,string id)
    {
        var source=CapabilitySource(shape!="async-on-sync",false,shape=="ambiguous"?"s---":"---s",false);var oracle=source.IndexOf("namespace App {public static class Oracle",StringComparison.Ordinal);source=source[..oracle];
        var dispatch=source.IndexOf("public static object? Dispatch",StringComparison.Ordinal);source=source.Remove(dispatch,source.IndexOf('}',dispatch)-dispatch+1);
        source=shape switch{
            "ambiguous"=>source.Replace("Hook0:ISendRequestTransform<Ping>","Hook0:ISendRequestTransform<Ping>,IAsyncSendRequestTransform<Ping>").Replace("private readonly Probe _p;public Hook0","public ValueTask<Ping> BeforeAsync(Ping r,CancellationToken t)=>new(r);private readonly Probe _p;public Hook0"),
            "async-on-sync"=>source.Replace("Hook0:ISendFinallyObserver<Ping>","Hook0:IAsyncSendFinallyObserver<Ping>").Replace("public void Finally(Ping r,SendExitInfo e,CancellationToken t)=>_p.Finally(0,r,e,t);","public ValueTask FinallyAsync(Ping r,SendExitInfo e,CancellationToken t)=>_p.FinallyAsync(0,r,e,t);"),
            _=>source.Replace("Stage.Hook<Hook0>()","Stage.Hook<Hook0,global::Zendiator.Generator.Tests.MetadataBorrowedReceiverFactory<Ping,Hook0>>(),Stage.Hook<Hook1>()")};
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"CapabilityRejected"+shape),out var output,out var diagnostics);if(shape=="metadata-session"){Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));Assert.Contains(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName.Contains("Canonical"));return;}Assert.Equal(id,Assert.Single(diagnostics).Id);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName.Contains("Canonical"));
        WriteNestedHookEvidence("capability-boundaries",shape,new{source,id,generatedExecution=false});
    }
    [Fact]
    public void Send_capabilities_body_edit_preserves_equality_and_unused_state_is_omitted()
    {
        var source=CapabilitySource(true,false,"saas",false);var driver=SyncDriver().RunGenerators(SyncCompilation(source,"CapabilityCache"));var text=driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.Capabilities.g.cs").SourceText.ToString();driver=driver.RunGenerators(SyncCompilation(source.Replace("new Ping(r.Value+1)","new Ping(r.Value+2)"),"CapabilityCache"));Assert.Equal(text,driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.Capabilities.g.cs").SourceText.ToString());Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalCapabilitySourceEmission"].SelectMany(s=>s.Outputs),o=>Assert.Equal(IncrementalStepRunReason.Cached,o.Reason));
        driver=SyncDriver().RunGenerators(SyncCompilation(CapabilitySource(true,true,"--s-",false),"NoResultState"));text=driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.Capabilities.g.cs").SourceText.ToString();Assert.DoesNotContain(" response;",text);Assert.DoesNotContain("success0",text);Assert.DoesNotContain("error0",text);WriteNestedHookEvidence("capability-incremental","body-state",new{bodyCached=true,unusedStateOmitted=true});
    }
    [Fact]
    public void Send_capabilities_preserve_accepted_full_Hook_and_Wrapper_generation_only()
    {HookAsyncFixture("async-owned",true);MixedAsyncFixture("async-owned","async-owned",false);WrapperRuntimeFixture(true,false,"async-owned","both");}
}
