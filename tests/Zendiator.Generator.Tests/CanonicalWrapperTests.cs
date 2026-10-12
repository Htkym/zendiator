using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class WrapperProbe
{
    public static readonly AsyncLocal<string?> Ambient=new();
    public readonly ConcurrentQueue<string> Trace=new();
    public readonly ConcurrentQueue<(string Phase,object Request,CancellationToken Token,Exception? Error,SendExitInfo? Exit,string? Ambient)> Calls=new();
    public readonly ConcurrentDictionary<object,int> Created=new(ReferenceEqualityComparer.Instance),Released=new(ReferenceEqualityComparer.Instance);
    public readonly ConcurrentDictionary<object,object> HandlerRequests=new(ReferenceEqualityComparer.Instance);
    public readonly ConcurrentDictionary<int,AsyncOnceGate<int>> Gates=new();
    public readonly CancellationTokenSource Cancellation=new(),Foreign=new();
    public string Mode="once";public bool Pending,FailFirst,CleanupFails,RequireOwnedCleanup;
    public readonly Exception BodyError=new("body"),CleanupError=new("cleanup"),WrapperError=new("wrapper");
    public Exception? Caught;public int WrapperDisposals,FactoryDisposals,HookDisposals;
    public void Construct(object h){Trace.Enqueue("handler.ctor");Assert.True(Created.TryAdd(h,1));}
    public void Hit(string phase,object r,CancellationToken t,Exception? error=null,SendExitInfo? exit=null)
    {
        if(RequireOwnedCleanup&&phase is "h1.after" or "h1.exception")Assert.Contains(Released.Keys,h=>HandlerRequests.TryGetValue(h,out var request)&&ReferenceEquals(request,r));
        Trace.Enqueue(phase);Calls.Enqueue((phase,r,t,error,exit,Ambient.Value));Ambient.Value=phase;
    }
    public int Handle(object h,object r,int value,CancellationToken t)
    {HandlerRequests[h]=r;Hit("handler",r,t);if(FailFirst&&value<20)throw BodyError;return value;}
    public ValueTask<int> HandleAsync(object h,object r,int value,CancellationToken t)
    {var result=Handle(h,r,value,t);if(!Pending)return new(result);var g=new AsyncOnceGate<int>(result);Assert.True(Gates.TryAdd(value,g));return g.Task;}
    public ValueTask HandleVoidAsync(object h,object r,int value,CancellationToken t)
    {Handle(h,r,value,t);if(!Pending)return default;var g=new AsyncOnceGate<int>(0);Assert.True(Gates.TryAdd(value,g));return g.Void;}
    public void Release(object h)
    {Trace.Enqueue("handler.dispose");Assert.True(Released.TryAdd(h,1));if(CleanupFails)throw CleanupError;}
    public ValueTask ReleaseAsync(object h){Release(h);return default;}
    public H Create<H>(object r,CancellationToken t,Func<H> create) where H:class {Hit("factory",r,t);return create();}
    public ValueTask<H> CreateAsync<H>(object r,CancellationToken t,Func<H> create) where H:class=>new(Create(r,t,create));
    public void DisposeWrapper(){Trace.Enqueue("wrapper.dispose");WrapperDisposals++;}
    public void DisposeFactory(){Trace.Enqueue("factory.dispose");FactoryDisposals++;}
    public void DisposeHook(){Trace.Enqueue("hook.dispose");HookDisposals++;}
}
public sealed class MetadataWrapper<R>:IAsyncSendWrapper<R,int> where R:class
{public ValueTask<int> InvokeAsync<TNext>(R r,TNext next,CancellationToken t) where TNext:IAsyncSendContinuation<R,int> =>next.InvokeAsync(r,t);}
public sealed class MetadataOwnedReceiverFactory<R,H>:IOwnedSessionFactory<R,H> where R:class where H:class
{public H Create(R r,CancellationToken t)=>throw new InvalidOperationException("Metadata fixture only.");}
public sealed class MetadataSyncWrapper<R>:ISendWrapper<R,int> where R:class
{public int Invoke<TNext>(R r,TNext next,CancellationToken t)where TNext:ISendContinuation<R,int> =>next.Invoke(r,t);}

public sealed partial class GeneratorTests
{
    private sealed record WrapperFixture(Assembly Assembly,Type Mediator,Type Request,Type Wrapper,Type Outer,Type Inner,Type Handler,Type Factory,MethodInfo Register,MethodInfo Dispatch);
    private static readonly ConcurrentDictionary<(bool Async,bool Void,string Receiver,string Hooks),Lazy<WrapperFixture>> WrapperFixtures=new();
    private static string WrapperSource(bool async,bool isVoid,string receiver,string hooks)
    {
        var req=async?"IRequest":"ISyncRequest";var hc=async?"IRequestHandler":"ISyncRequestHandler";var result=isVoid?"":",int";var family=async?"SendAsync":"SendSync";
        var ctorDispose=async?"IAsyncDisposable":"IDisposable";var disposable=async?"ValueTask IAsyncDisposable.DisposeAsync()":"void IDisposable.Dispose()";var disposeBody=async?"{_p.DisposeWrapper();return default;}":"=>_p.DisposeWrapper();";
        var prefix=async?"IAsyncSend":"ISend";var hookAfter=isVoid?prefix+"SuccessObserver<Ping>":prefix+"ResponseTransform<Ping,int>";
        var factoryAsync=receiver.StartsWith("async-",StringComparison.Ordinal);var role="I"+(factoryAsync?"Async":"")+(receiver.EndsWith("owned",StringComparison.Ordinal)?"Owned":"Borrowed")+"SessionFactory";
        var pipeline=(hooks is "outer" or "both"?"Stage.Hook<Hook<OuterTag>>(),":"")+"Stage.Wrapper<Wrap>()"+(hooks is "inner" or "both"?",Stage.Hook<Hook<InnerTag>>()":"");
        var body=WrapperUserBody(async,isVoid);
        var factory=receiver=="direct"?"":$$"""
            public sealed class Factory:{{role}}<Ping,Handler>,IDisposable {
                private readonly Probe _p;public Factory(Probe p){_p=p;}
                public {{(factoryAsync?"ValueTask<Handler>":"Handler")}} {{(factoryAsync?"CreateAsync":"Create")}}(Ping r,CancellationToken t)=>_p.{{(factoryAsync?"CreateAsync":"Create")}}(r,t,()=>new Handler(_p));
                public void Dispose()=>_p.DisposeFactory();
            }
            """;
        return $$"""
            using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.WrapperProbe;
            namespace App {
                public sealed record Ping(int Value):{{req}}{{(isVoid?"":"<int>")}};public sealed class OuterTag;public sealed class InnerTag;
                public sealed class Hook<T>:{{prefix}}RequestTransform<Ping>,{{hookAfter}},{{prefix}}ExceptionObserver<Ping>,{{prefix}}FinallyObserver<Ping>,{{ctorDispose}} where T:class {
                    private readonly Probe _p;private readonly string _id;public Hook(Probe p){_p=p;_id=typeof(T)==typeof(OuterTag)?"h0":"h1";}
                    public {{(async?"ValueTask<Ping>":"Ping")}} {{(async?"BeforeAsync":"Before")}}(Ping r,CancellationToken t){_p.Hit(_id+".before",r,t);return {{(async?"new(new Ping(r.Value+1))":"new Ping(r.Value+1)")}};}
                    public {{(async?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int"))}} {{(async?"AfterAsync":"After")}}(Ping r,{{(isVoid?"":"int value,")}}CancellationToken t){_p.Hit(_id+".after",r,t);{{(isVoid?(async?"return default;":""):(async?"return new(value+1);":"return value+1;"))}}}
                    public {{(async?"ValueTask":"void")}} {{(async?"OnExceptionAsync":"OnException")}}(Ping r,Exception e,CancellationToken t){_p.Hit(_id+".exception",r,t,e);{{(async?"return default;":"")}}}
                    public {{(async?"ValueTask":"void")}} {{(async?"FinallyAsync":"Finally")}}(Ping r,SendExitInfo e,CancellationToken t){_p.Hit(_id+".finally",r,t,exit:e);{{(async?"return default;":"")}}}
                    {{disposable}} {{(async?"{_p.DisposeHook();return default;}":"=>_p.DisposeHook();")}}
                }
                [ZendiatorHandler]public sealed class Handler:{{hc}}<Ping{{result}}>,{{ctorDispose}} {
                    private readonly Probe _p;public Handler(Probe p){_p=p;p.Construct(this);}
                    public {{(async?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int"))}} {{(async?"HandleAsync":"Handle")}}(Ping r,CancellationToken t){ {{(async?"return _p."+(isVoid?"HandleVoidAsync":"HandleAsync")+"(this,r,r.Value,t);":(isVoid?"":"return ")+"_p.Handle(this,r,r.Value,t);")}} }
                    {{disposable}} {{(async?"=>_p.ReleaseAsync(this);":"=>_p.Release(this);")}}
                }
                public sealed class Wrap:{{prefix}}Wrapper<Ping{{result}}>,{{ctorDispose}} {
                    private readonly Probe _p;public Wrap(Probe p){_p=p;}
                    {{body}}
                    {{disposable}} {{disposeBody}}
                }
                {{factory}}
                [ZendiatorComposition("wrapper")]public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{c.Handlers<Ping>(DispatchFamily.{{family}},{{(receiver=="direct"?"typeof(Handler)":"HandlerEntry.Session<Handler,Factory>()")}});c.Pipeline<Ping>(DispatchFamily.{{family}},{{pipeline}});});
                    public static object? Dispatch(object m,object? r,CancellationToken t){ {{(isVoid&&!async?"":"return ")}}((global::App.Generated.Reads.IZendiator)m).{{(async?"SendAsync":"Send")}}((Ping)r!,t);{{(isVoid&&!async?"return null;":"")}} }
                }
            }
            """;
    }
    private static string WrapperUserBody(bool async,bool isVoid)
    {
        var res=isVoid?"":",int";var result=async?(isVoid?"ValueTask":"ValueTask<int>"):(isVoid?"void":"int");var call=async?"InvokeAsync":"Invoke";var continuation=(async?"IAsyncSendContinuation":"ISendContinuation")+"<Ping"+res+">";
        var awaitCall=async?"await ":"";var cfg=async?".ConfigureAwait(false)":"";
        var ending=isVoid?"return;":"return value+1;";
        var common=$$"""
            _p.Hit("wrapper.before",r,t);
            if(_p.Mode=="skip"){_p.Hit("wrapper.skip",r,t);{{(isVoid?"return;":"return 91;")}}}
            if(_p.Mode=="throw")throw _p.WrapperError;
            var token=_p.Mode=="replace"||_p.Mode=="replace-cancel"?_p.Foreign.Token:t;
            if(_p.Mode=="replace"||_p.Mode=="replace-cancel")r=new Ping(20);
            {{(isVoid?"":"int value;")}}
            if(_p.Mode=="retry"||_p.Mode=="cancel-recover"){
                try{ {{(isVoid?"":"_ = ")}}{{awaitCall}}next.{{call}}(r,_p.Mode=="cancel-recover"?_p.Foreign.Token:token){{cfg}}; }
                catch(Exception e){_p.Caught=e;_p.Hit("wrapper.catch",r,t,e);}
                r=new Ping(20);{{(isVoid?"":"value=")}}{{awaitCall}}next.{{call}}(r,token){{cfg}};
            }else if(_p.Mode=="twice"||_p.Mode=="overlap"){
                {{(async?"var a=next.InvokeAsync(new Ping(10),token);var b=next.InvokeAsync(new Ping(20),token);":"")}}
                {{(async?(isVoid?"await a.ConfigureAwait(false);await b.ConfigureAwait(false);":"value=await a.ConfigureAwait(false)+await b.ConfigureAwait(false);"):(isVoid?"next.Invoke(new Ping(10),token);next.Invoke(new Ping(20),token);":"value=next.Invoke(new Ping(10),token)+next.Invoke(new Ping(20),token);"))}}
            }else { {{(isVoid?"":"value=")}}{{awaitCall}}next.{{call}}(r,token){{cfg}}; }
            _p.Hit("wrapper.after",r,t);{{ending}}
            """;
        if(!async)return $$"""public {{result}} Invoke<TNext>(Ping r,TNext next,CancellationToken t) where TNext:{{continuation}} { {{common}} }""";
        return $$"""
            public {{result}} InvokeAsync<TNext>(Ping r,TNext next,CancellationToken t) where TNext:{{continuation}} {
                if(_p.Mode=="pass"){_p.Hit("wrapper.before",r,t);var value=next.InvokeAsync(r,t);_p.Hit("wrapper.return",r,t);return value;}
                return Native(r,next,t);
            }
            private async {{result}} Native<TNext>(Ping r,TNext next,CancellationToken t) where TNext:{{continuation}} { {{common}} }
            """;
    }
    private static WrapperFixture WrapperRuntimeFixture(bool async,bool isVoid,string receiver,string hooks)=>WrapperFixtures.GetOrAdd((async,isVoid,receiver,hooks),key=>new(()=>
    {
        var source=WrapperSource(key.Async,key.Void,key.Receiver,key.Hooks);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"Wrapper_"+Guid.NewGuid().ToString("N")),out var output,out var diagnostics);
        Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var sources=driver.GetRunResult().Results.Single().GeneratedSources;Assert.Equal(2,sources.Length);
        var evidence=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");if(!string.IsNullOrEmpty(evidence))
        {var folder=Path.Combine(evidence,"wrapper-"+key.Async+"-"+key.Void+"-"+key.Receiver+"-"+key.Hooks);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"input.cs"),source);foreach(var s in sources)File.WriteAllText(Path.Combine(folder,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(folder,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(folder,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=diagnostics.Length}));}
        bytes.Position=0;var a=AssemblyLoadContext.Default.LoadFromStream(bytes);var bridge=a.GetType("App.Bridge")!;
        return new(a,a.GetType("App.Generated.Reads.Zendiator")!,a.GetType("App.Ping")!,a.GetType("App.Wrap")!,a.GetType("App.Hook`1")!.MakeGenericType(a.GetType("App.OuterTag")!),a.GetType("App.Hook`1")!.MakeGenericType(a.GetType("App.InnerTag")!),a.GetType("App.Handler")!,a.GetType("App.Factory")??a.GetType("App.Handler")!,bridge.GetMethod("Register")!,bridge.GetMethod("Dispatch")!);
    })).Value;
    private static object? InvokeWrapper(WrapperFixture f,bool async,bool isVoid,string receiver,string hooks,bool generated,IServiceProvider p,object? r,CancellationToken t)
    {
        try{
            if(generated)return f.Dispatch.Invoke(null,[p.GetRequiredService(f.Mediator),r,t]);
            Type lease=async?MixedLeaseType(receiver,f.Request,f.Handler,f.Factory):(receiver switch{"direct"=>typeof(SyncDiLease<,>).MakeGenericType(f.Request,f.Handler),"owned"=>typeof(SyncOwnedLease<,,>).MakeGenericType(f.Request,f.Handler,f.Factory),_=>typeof(SyncBorrowedLease<,,>).MakeGenericType(f.Request,f.Handler,f.Factory)});
            var types=new List<Type>{f.Request};if(!isVoid)types.Add(typeof(int));types.AddRange([f.Wrapper,f.Outer,f.Inner,f.Handler,lease]);
            return typeof(CanonicalWrapperReference).GetMethod("Send"+(async?"Async":"Sync")+(isVoid?"Void":"Response"),BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(types.ToArray()).Invoke(null,[p,r,t,hooks is "outer" or "both",hooks is "inner" or "both"]);
        }catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}
    }
    public static IEnumerable<object[]> WrapperShapes()
    {
        foreach(var a in new[]{false,true})foreach(var v in new[]{false,true})foreach(var receiver in (a?new[]{"direct","owned","borrowed","async-owned","async-borrowed"}:new[]{"direct","owned","borrowed"}))yield return [a,v,receiver,"none"];
        foreach(var a in new[]{false,true})foreach(var v in new[]{false,true})yield return [a,v,a?"async-owned":"owned","both"];
        yield return [false,false,"owned","outer"];yield return [true,true,"async-borrowed","inner"];
    }
    [Theory][MemberData(nameof(WrapperShapes))]
    public async Task Opaque_wrapper_reference_generation_API_preserve_boundary_order_and_ownership(bool async,bool isVoid,string receiver,string hooks)
    {var f=WrapperRuntimeFixture(async,isVoid,receiver,hooks);foreach(var generated in new[]{false,true})await RunWrapperCase(f,async,isVoid,receiver,hooks,generated,"once");}
    [Theory]
    [InlineData(true,true,"skip")][InlineData(false,true,"twice")][InlineData(true,false,"twice")]
    [InlineData(false,false,"retry")][InlineData(true,true,"retry")][InlineData(false,true,"replace")][InlineData(true,false,"replace")]
    [InlineData(false,false,"throw")][InlineData(true,true,"throw")][InlineData(false,true,"cleanup")][InlineData(true,false,"cleanup")]
    [InlineData(true,false,"pass")][InlineData(false,false,"pre-cancel")][InlineData(true,true,"pre-cancel")]
    [InlineData(false,false,"replace-cancel")][InlineData(true,true,"replace-cancel")]
    [InlineData(false,true,"cancel-recover")][InlineData(true,false,"cancel-recover")]
    public async Task Opaque_wrapper_controls_zero_repeat_retry_replacement_failure_and_context(bool async,bool isVoid,string mode)
    {var receiver=async?"async-owned":"owned";var f=WrapperRuntimeFixture(async,isVoid,receiver,"both");foreach(var generated in new[]{false,true})await RunWrapperCase(f,async,isVoid,receiver,"both",generated,mode);}
    [Theory][InlineData(false)]
    public async Task Opaque_wrapper_overlapping_next_calls_have_independent_inner_leases_and_single_consumption(bool isVoid)
    {var f=WrapperRuntimeFixture(true,isVoid,"async-owned","both");foreach(var generated in new[]{false,true})await RunWrapperCase(f,true,isVoid,"async-owned","both",generated,"overlap");}

    private static async Task RunWrapperCase(WrapperFixture f,bool async,bool isVoid,string receiver,string hooks,bool generated,string mode)
    {
        var p=new WrapperProbe{Mode=mode=="cleanup"?"once":mode,FailFirst=mode=="retry",CleanupFails=mode=="cleanup",Pending=mode=="overlap",RequireOwnedCleanup=receiver.EndsWith("owned",StringComparison.Ordinal)};var ambient=WrapperProbe.Ambient.Value;
        try{
            if(mode=="pre-cancel")p.Cancellation.Cancel();if(mode is "replace-cancel" or "cancel-recover")p.Foreign.Cancel();IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);
            Assert.Contains(services,d=>d.ServiceType==f.Wrapper&&d.Lifetime==ServiceLifetime.Scoped);if(receiver!="direct")Assert.DoesNotContain(services,d=>d.ServiceType==f.Handler);
            await using var provider=services.BuildServiceProvider();var request=Activator.CreateInstance(f.Request,[10])!;Exception? error=null;object? result=null;
            await using(var scope=provider.CreateAsyncScope()){
                WrapperProbe.Ambient.Value="caller";Assert.Throws<ArgumentNullException>(()=>InvokeWrapper(f,async,isVoid,receiver,hooks,generated,scope.ServiceProvider,null,default));Assert.Empty(p.Trace);
                if(async){var task=AsyncTask(InvokeWrapper(f,true,isVoid,receiver,hooks,generated,scope.ServiceProvider,request,p.Cancellation.Token)!,isVoid);Assert.Equal("caller",WrapperProbe.Ambient.Value);
                    if(mode=="overlap"){Assert.False(task.IsCompleted);Assert.Equal(2,p.Gates.Count);Assert.Equal(2,p.Created.Count);p.Gates.Values.Single(g=>g.Consumptions==0&&p.Gates.First(k=>ReferenceEquals(k.Value,g)).Key>15).Complete();p.Gates.Values.Single(g=>g.Consumptions==0&&p.Gates.First(k=>ReferenceEquals(k.Value,g)).Key<15).Complete();}
                    try{await task.WaitAsync(TimeSpan.FromSeconds(10));if(!isVoid)result=await (Task<int>)task;}catch(Exception e){error=e;}
                    Assert.Equal(error is OperationCanceledException,task.IsCanceled);Assert.Equal(error!=null&&error is not OperationCanceledException,task.IsFaulted);
                }else{try{result=InvokeWrapper(f,false,isVoid,receiver,hooks,generated,scope.ServiceProvider,request,p.Cancellation.Token);}catch(Exception e){error=e;}}
                var count=mode is "skip" or "throw" or "pre-cancel" or "replace-cancel"?0:mode is "twice" or "retry" or "overlap"?2:1;
                Assert.Equal(count,p.Calls.Count(c=>c.Phase=="handler"));if(mode=="throw")Assert.Same(p.WrapperError,error);else if(mode=="cleanup")Assert.Same(p.CleanupError,error);else if(mode is "pre-cancel" or "replace-cancel"){Assert.IsAssignableFrom<OperationCanceledException>(error);Assert.Equal(mode=="pre-cancel"?p.Cancellation.Token:p.Foreign.Token,((OperationCanceledException)error!).CancellationToken);}else Assert.Null(error);
                if(mode=="retry")Assert.Same(p.BodyError,p.Caught);Assert.All(p.Gates.Values,g=>Assert.Equal(1,g.Consumptions));
                if(mode=="cancel-recover"){Assert.IsAssignableFrom<OperationCanceledException>(p.Caught);Assert.Equal(p.Foreign.Token,((OperationCanceledException)p.Caught!).CancellationToken);}
                var owned=receiver.EndsWith("owned",StringComparison.Ordinal);Assert.Equal(owned?p.Created.Count:0,p.Released.Count);Assert.Equal(0,p.WrapperDisposals);Assert.Equal(0,p.FactoryDisposals);Assert.Equal(0,p.HookDisposals);
                var calls=p.Calls.ToArray();var outerAfter=calls.FirstOrDefault(c=>c.Phase=="h0.after");
                if(outerAfter.Phase!=null){Assert.Equal(p.Cancellation.Token,outerAfter.Token);if(mode=="replace")Assert.Equal(11,(int)outerAfter.Request.GetType().GetProperty("Value")!.GetValue(outerAfter.Request)!);
                    if(async)Assert.Equal(mode=="pass"?"wrapper.return":"h0.before",outerAfter.Ambient);else Assert.Equal(mode=="skip"?"wrapper.skip":"wrapper.after",outerAfter.Ambient);}
                if(mode=="replace")Assert.All(calls.Where(c=>c.Phase is "handler" or "h1.before" or "h1.after" or "h1.finally"),c=>Assert.Equal(p.Foreign.Token,c.Token));
                if(count==0)Assert.Empty(p.Created);
                if(mode is "cleanup" or "throw" or "replace-cancel")Assert.Same(error,calls.Single(c=>c.Phase=="h0.exception").Error);
                if(error==null&&!isVoid){var expected=mode=="skip"?92:mode is "twice" or "overlap"?36:mode is "retry" or "replace" or "cancel-recover"?24:mode=="pass"?14:hooks=="both"?15:hooks=="none"?11:13;Assert.Equal(expected,result);}
                foreach(var finalCall in calls.Where(c=>c.Exit!=null)){
                    var observed=calls.LastOrDefault(c=>c.Phase==finalCall.Phase.Replace("finally","exception",StringComparison.Ordinal)&&ReferenceEquals(c.Request,finalCall.Request));
                    Assert.Equal(observed.Error==null,finalCall.Exit!.Value.Success);Assert.Same(observed.Error,finalCall.Exit!.Value.Error);
                }
                if(mode!="overlap")Assert.Equal(WrapperExpectedPhases(receiver,hooks,mode),calls.Select(c=>c.Phase));
                if(owned&&count!=0)Assert.Equal(count,p.Released.Count);
                WriteNestedHookEvidence("wrapper-observations",async+"-"+isVoid+"-"+receiver+"-"+hooks+"-"+generated+"-"+mode,new{async,isVoid,receiver,hooks,generated,mode,result,error=error?.Message,trace=p.Trace.ToArray(),handlerCalls=count,created=p.Created.Count,operationReleases=p.Released.Count,consumptions=p.Gates.Values.Select(g=>g.Consumptions),calls=calls.Select(c=>new{c.Phase,value=c.Request.GetType().GetProperty("Value")!.GetValue(c.Request),token=c.Token==p.Foreign.Token?"foreign":"root",error=c.Error?.Message,success=c.Exit?.Success,exitError=c.Exit?.Error?.Message,c.Ambient})});p.CleanupFails=false;
            }
            if(receiver is "borrowed" or "async-borrowed")foreach(var h in p.Created.Keys){if(async)await ((IAsyncDisposable)h).DisposeAsync();else ((IDisposable)h).Dispose();}
            Assert.Equal(p.Created.Count,p.Released.Count);Assert.All(p.Released.Values,n=>Assert.Equal(1,n));Assert.Equal(mode=="pre-cancel"?0:1,p.WrapperDisposals);
            WriteNestedHookEvidence("wrapper-lifetime",async+"-"+isVoid+"-"+receiver+"-"+hooks+"-"+generated+"-"+mode,new{async,isVoid,receiver,hooks,generated,mode,trace=p.Trace.ToArray(),created=p.Created.Count,released=p.Released.Count,p.WrapperDisposals,p.FactoryDisposals,p.HookDisposals});
        }finally{p.Cancellation.Dispose();p.Foreign.Dispose();WrapperProbe.Ambient.Value=ambient;}
    }
    private static string[] WrapperExpectedPhases(string receiver,string hooks,string mode)
    {
        if(mode=="pre-cancel")return [];var phases=new List<string>();var outer=hooks is "outer" or "both";var inner=hooks is "inner" or "both";
        if(outer)phases.Add("h0.before");phases.Add("wrapper.before");
        if(mode is "throw" or "replace-cancel"){if(outer)phases.AddRange(["h0.exception","h0.finally"]);return phases.ToArray();}
        if(mode=="skip")phases.Add("wrapper.skip");
        else
        {
            var calls=mode is "twice" or "retry"?2:1;if(mode=="cancel-recover")phases.Add("wrapper.catch");
            for(var i=0;i<calls;i++){
                if(inner)phases.Add("h1.before");if(receiver!="direct")phases.Add("factory");phases.Add("handler");
                var failed=mode=="cleanup"||mode=="retry"&&i==0;if(inner)phases.AddRange([failed?"h1.exception":"h1.after","h1.finally"]);
                if(mode=="cleanup"){if(outer)phases.AddRange(["h0.exception","h0.finally"]);return phases.ToArray();}
                if(mode=="retry"&&i==0)phases.Add("wrapper.catch");
            }
            phases.Add(mode=="pass"?"wrapper.return":"wrapper.after");
        }
        if(outer)phases.AddRange(["h0.after","h0.finally"]);return phases.ToArray();
    }
    [Theory][InlineData("metadata-hook")][InlineData("metadata")]
    public void Opaque_wrapper_unsupported_scope_shapes_keep_diagnostic_and_no_execution(string shape)
        =>AssertWrapperUnsupported(shape);
    private static void AssertWrapperUnsupported(string shape)
    {
        var source=WrapperSource(true,false,"direct","none");var start=source.IndexOf("public static object? Dispatch",StringComparison.Ordinal);var end=source.IndexOf('}',start);source=source.Remove(start,end-start+1);
        source=shape switch{
            "metadata-factory"=>source.Replace("Stage.Wrapper<Wrap>()","Stage.Wrapper<Wrap,global::Zendiator.Generator.Tests.MetadataOwnedReceiverFactory<Ping,Wrap>>()"),
            "metadata"=>source.Replace("Stage.Wrapper<Wrap>()","Stage.Wrapper<global::Zendiator.Generator.Tests.MetadataWrapper<Ping>>()"),
            _=>source.Replace("Stage.Wrapper<Wrap>()","Stage.Hook<global::Zendiator.Generator.Tests.MetadataAsyncHook<Ping>>(),Stage.Wrapper<Wrap>()")};
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"WrapperUnsupported"+shape),out var output,out var diagnostics);
        Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));Assert.Contains(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName=="Zendiator.Canonical.Wrappers.g.cs");
        WriteNestedHookEvidence("wrapper-boundaries",shape,new{shape,source,diagnostics=diagnostics.Select(d=>new{d.Id,message=d.GetMessage()}),generatedExecution=false});
    }
    [Fact]
    public async Task Opaque_wrapper_corrected_sync_skip_preserves_zero_next_context_and_order()
    {var f=WrapperRuntimeFixture(false,false,"owned","both");foreach(var generated in new[]{false,true})await RunWrapperCase(f,false,false,"owned","both",generated,"skip");}
    [Fact]
    public async Task Opaque_wrapper_corrected_void_overlap_checks_ownership_per_inner_request()
    {var f=WrapperRuntimeFixture(true,true,"async-owned","both");foreach(var generated in new[]{false,true})await RunWrapperCase(f,true,true,"async-owned","both",generated,"overlap");}
    [Fact]
    public void Opaque_wrapper_metadata_factory_region_remains_diagnosed()=>AssertWrapperUnsupported("metadata-factory");
    [Fact]
    public void Opaque_wrapper_body_edit_preserves_emission_cache_and_region_structure()
    {
        var source=WrapperSource(true,false,"async-owned","both");var driver=SyncDriver().RunGenerators(SyncCompilation(source,"WrapperCache"));var body=driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.Wrappers.g.cs").SourceText.ToString();
        driver=driver.RunGenerators(SyncCompilation(source.Replace("return value+1;","return value+2;"),"WrapperCache"));
        Assert.Equal(body,driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.Wrappers.g.cs").SourceText.ToString());Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalWrapperSourceEmission"].SelectMany(s=>s.Outputs),o=>Assert.Equal(IncrementalStepRunReason.Cached,o.Reason));
        Assert.Contains("ExecuteOuter",body);Assert.Contains("ExecuteInner",body);WriteNestedHookEvidence("wrapper-incremental","body",new{cached=true,explicitRegions=2});
    }
    [Fact]
    public void Opaque_wrapper_shared_registration_preserves_selected_accepted_Hook_generation_only()
    {CompileSyncFixture("owned",false);HookAsyncFixture("async-owned",true);MixedAsyncFixture("async-owned","async-owned",false);}
}
