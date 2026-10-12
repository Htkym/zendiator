using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed class AsyncHookProbe
{
    public static readonly AsyncLocal<string?> Ambient = new();
    public readonly CancellationTokenSource Cancellation = new();
    public readonly List<string> Trace = [];
    public readonly List<(string Phase,object? Request,Exception? Error,SendExitInfo? Exit,string? Ambient)> Calls = [];
    public readonly Dictionary<string,Exception> Failures = [];
    public readonly List<object> Hooks = [], ReleasedHooks = [];
    public readonly List<IAsyncOnceGate> Gates = [];
    public string? Pending, NativePhase, CancelPhase;
    public bool NullBefore, NullFactory;
    public int HandlerDisposals, FactoryDisposals;
    public void CheckToken(CancellationToken token)=>Assert.Equal(Cancellation.Token,token);
    public void HookConstruct(object hook,int id) {Trace.Add($"h{id}.construct");Hooks.Add(hook);}
    public void HandlerConstruct()=>Trace.Add("handler.construct");
    public void FactoryConstruct()=>Trace.Add("factory.construct");
    private void Hit(string phase,object? request,Exception? error=null,SendExitInfo? exit=null)
    {
        Trace.Add(phase);Calls.Add((phase,request,error,exit,Ambient.Value));Ambient.Value=phase;
        if(CancelPhase==phase)Cancellation.Cancel();
        if(Failures.TryGetValue(phase,out var failure))throw failure;
    }
    private ValueTask<T> Step<T>(string phase,object? request,T value,Exception? error=null,SendExitInfo? exit=null)
    {
        if(NativePhase==phase)return Native(phase,request,value,error,exit);
        Hit(phase,request,error,exit);
        if(Pending!=phase)return new(value);
        var gate=new AsyncOnceGate<T>(value);Gates.Add(gate);return gate.Task;
    }
    private async ValueTask<T> Native<T>(string phase,object? request,T value,Exception? error,SendExitInfo? exit)
    {Hit(phase,request,error,exit);await Task.CompletedTask.ConfigureAwait(false);return value;}
    private ValueTask StepVoid(string phase,object? request,Exception? error=null,SendExitInfo? exit=null)
    {
        Hit(phase,request,error,exit);
        if(Pending!=phase)return default;
        var gate=new AsyncOnceGate<int>(0);Gates.Add(gate);return gate.Void;
    }
    public ValueTask<R> Before<R>(int id,R request,R next) where R:class=>Step($"h{id}.before",request,NullBefore?null!:next);
    public ValueTask<int> After(int id,object? request,int value)=>Step($"h{id}.after",request,value+1);
    public ValueTask AfterVoid(int id,object? request)=>StepVoid($"h{id}.after",request);
    public ValueTask Observe(int id,object? request,Exception error)=>StepVoid($"h{id}.exception",request,error);
    public ValueTask Finally(int id,object? request,SendExitInfo exit)=>StepVoid($"h{id}.finally",request,exit:exit);
    public ValueTask<int> Handle(object request,int value)=>Step("handler",request,value);
    public ValueTask HandleVoid(object request)=>StepVoid("handler",request);
    public H Create<H>(object request,Func<H> create) where H:class
    {Hit("factory",request);return NullFactory?null!:create();}
    public ValueTask<H> CreateAsync<H>(object request,Func<H> create) where H:class
    {
        var value=Create(request,create);if(Pending!="factory")return new(value);
        var gate=new AsyncOnceGate<H>(value);Gates.Add(gate);return gate.Task;
    }
    public ValueTask DisposeHook(object hook,int id)
    {
        Assert.DoesNotContain(ReleasedHooks,h=>ReferenceEquals(h,hook));ReleasedHooks.Add(hook);return StepVoid($"h{id}.cleanup",null);
    }
    public ValueTask DisposeHandler(){Trace.Add("handler.dispose");HandlerDisposals++;return default;}
    public void DisposeFactory(){Trace.Add("factory.dispose");FactoryDisposals++;}
}

// Metadata-only stage for the source-declaration boundary test.
public sealed class MetadataAsyncHook<R>:IAsyncSendRequestTransform<R>,IAsyncSendResponseTransform<R,int>,IAsyncSendExceptionObserver<R>,IAsyncSendFinallyObserver<R> where R:class
{
    public ValueTask<R> BeforeAsync(R r,CancellationToken t)=>new(r);
    public ValueTask<int> AfterAsync(R r,int value,CancellationToken t)=>new(value);
    public ValueTask OnExceptionAsync(R r,Exception e,CancellationToken t)=>default;
    public ValueTask FinallyAsync(R r,SendExitInfo e,CancellationToken t)=>default;
}

public sealed partial class GeneratorTests
{
    private sealed record AsyncHookFixture(Assembly Assembly,Type Mediator,Type Request,Type Outer,Type Inner,Type Handler,Type Factory,MethodInfo Register,MethodInfo Dispatch);
    private static readonly ConcurrentDictionary<(string Mode,bool Void),Lazy<AsyncHookFixture>> AsyncHookFixtures=new();
    private static string AsyncHookSource(string mode,bool isVoid)
    {
        var hasFactory=mode is not ("direct" or "two");var isAsync=mode.StartsWith("async-",StringComparison.Ordinal);var owned=mode.EndsWith("owned",StringComparison.Ordinal);
        var role="I"+(isAsync?"Async":"")+(owned?"Owned":"Borrowed")+"SessionFactory";
        var handlerContract="IRequestHandler<Ping"+(isVoid?"":",int")+">";
        var afterContract=isVoid?"IAsyncSendSuccessObserver<Ping>":"IAsyncSendResponseTransform<Ping,int>";
        var stage=hasFactory?"Stage.Hook<Hook<OuterTag>,Factory<OuterTag>>()":"Stage.Hook<Hook<OuterTag>>()";
        if(mode=="two")stage+=",Stage.Hook<Hook<InnerTag>>()";
        var factory=hasFactory?$$"""
            public class FactoryBase<T> : {{role}}<Ping,Hook<T>>,IDisposable where T:class {
                private readonly Probe _probe;
                public FactoryBase(Probe probe){_probe=probe;probe.FactoryConstruct();}
                {{(isAsync?"ValueTask<Hook<T>>":"Hook<T>")}} {{role}}<Ping,Hook<T>>.{{(isAsync?"CreateAsync":"Create")}}(Ping r,CancellationToken t)
                    {_probe.CheckToken(t);return _probe.{{(isAsync?"CreateAsync":"Create")}}(r,()=>new Hook<T>(_probe));}
                void IDisposable.Dispose()=>_probe.DisposeFactory();
            }
            public sealed class Factory<T>:FactoryBase<T> where T:class{public Factory(Probe p):base(p){} }
            """:"";
        return $$"""
            using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.AsyncHookProbe;
            namespace App {
                public sealed record Ping(int Value):IRequest{{(isVoid?"":"<int>")}};
                public sealed class OuterTag;public sealed class InnerTag;
                public sealed class Hook<T>:IAsyncSendRequestTransform<Ping>,{{afterContract}},IAsyncSendExceptionObserver<Ping>,IAsyncSendFinallyObserver<Ping>,IAsyncDisposable where T:class {
                    private readonly Probe _probe;private readonly int _id;
                    public Hook(Probe p){_probe=p;_id=typeof(T)==typeof(InnerTag)?1:0;p.HookConstruct(this,_id);}
                    ValueTask<Ping> IAsyncSendRequestTransform<Ping>.BeforeAsync(Ping r,CancellationToken t){_probe.CheckToken(t);return _probe.Before(_id,r,new Ping(r.Value+1));}
                    {{(isVoid?"ValueTask":"ValueTask<int>")}} {{afterContract}}.AfterAsync(Ping r,{{(isVoid?"":"int value,")}}CancellationToken t){_probe.CheckToken(t);return _probe.{{(isVoid?"AfterVoid":"After")}}(_id,r{{(isVoid?"":",value")}});}
                    ValueTask IAsyncSendExceptionObserver<Ping>.OnExceptionAsync(Ping r,Exception e,CancellationToken t){_probe.CheckToken(t);return _probe.Observe(_id,r,e);}
                    ValueTask IAsyncSendFinallyObserver<Ping>.FinallyAsync(Ping r,SendExitInfo e,CancellationToken t){_probe.CheckToken(t);return _probe.Finally(_id,r,e);}
                    ValueTask IAsyncDisposable.DisposeAsync()=>_probe.DisposeHook(this,_id);
                }
                [ZendiatorHandler]public sealed class Handler:IRequestHandler<Ping{{(isVoid?"":",int")}}>,IAsyncDisposable {
                    private readonly Probe _probe;public Handler(Probe p){_probe=p;p.HandlerConstruct();}
                    {{(isVoid?"ValueTask":"ValueTask<int>")}} {{handlerContract}}.HandleAsync(Ping r,CancellationToken t){_probe.CheckToken(t);return _probe.{{(isVoid?"HandleVoid":"Handle")}}(r{{(isVoid?"":",r.Value")}});}
                    ValueTask IAsyncDisposable.DisposeAsync()=>_probe.DisposeHandler();
                }
                {{factory}}
                [ZendiatorComposition("async-hooks")]public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler));c.Pipeline<Ping>(DispatchFamily.SendAsync,{{stage}});});
                    public static object Dispatch(object mediator,object? request,CancellationToken token)=>((global::App.Generated.Reads.IZendiator)mediator).SendAsync((Ping)request!,token);
                }
            }
            """;
    }
    private static AsyncHookFixture HookAsyncFixture(string mode,bool isVoid)=>AsyncHookFixtures.GetOrAdd((mode,isVoid),key=>new(()=>
    {
        var source=AsyncHookSource(key.Mode,key.Void);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"AsyncHook_"+Guid.NewGuid().ToString("N")),out var output,out var diagnostics);
        Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));
        var sources=driver.GetRunResult().Results.Single().GeneratedSources;Assert.Equal(2,sources.Length);
        var evidence=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if(!string.IsNullOrEmpty(evidence))
        {
            var p=Path.Combine(evidence,"async-hook-"+key.Mode+"-"+key.Void);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);
            foreach(var s in sources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());
            File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=diagnostics.Length}));
        }
        bytes.Position=0;var assembly=AssemblyLoadContext.Default.LoadFromStream(bytes);var bridge=assembly.GetType("App.Bridge")!;var outer=assembly.GetType("App.Hook`1")!.MakeGenericType(assembly.GetType("App.OuterTag")!);
        return new(assembly,assembly.GetType("App.Generated.Reads.Zendiator")!,assembly.GetType("App.Ping")!,outer,assembly.GetType("App.Hook`1")!.MakeGenericType(assembly.GetType("App.InnerTag")!),assembly.GetType("App.Handler")!,assembly.GetType("App.Factory`1")?.MakeGenericType(assembly.GetType("App.OuterTag")!)??outer,bridge.GetMethod("Register")!,bridge.GetMethod("Dispatch")!);
    })).Value;
    private static object InvokeAsyncHook(AsyncHookFixture f,string mode,bool isVoid,bool generated,IServiceProvider provider,object? request,CancellationToken token)
    {
        try
        {
            if(generated)return f.Dispatch.Invoke(null,[provider.GetRequiredService(f.Mediator),request,token])!;
            var name="SendHook"+(mode switch{"direct"=>"Direct","two"=>"Two","owned"=>"Owned","borrowed"=>"Borrowed","async-owned"=>"AsyncOwned",_=>"AsyncBorrowed"})+(isVoid?"Void":"Response");
            var types=new List<Type>{f.Request};if(!isVoid)types.Add(typeof(int));types.Add(f.Outer);if(mode=="two")types.Add(f.Inner);types.Add(f.Handler);if(mode is not ("direct" or "two"))types.Add(f.Factory);
            return typeof(CanonicalAsyncHookReference).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(types.ToArray()).Invoke(null,[provider,request,token])!;
        }
        catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}
    }
    public static IEnumerable<object[]> AsyncHookShapes()=>from mode in new[]{"direct","owned","borrowed","async-owned","async-borrowed","two"} from isVoid in new[]{false,true} select new object[]{mode,isVoid};
    [Theory][MemberData(nameof(AsyncHookShapes))]
    public async Task Async_hook_standard_API_and_typed_reference_preserve_order_values_identity_and_ownership(string mode,bool isVoid)
    {
        var fixture=HookAsyncFixture(mode,isVoid);
        foreach(var generated in new[]{false,true})await RunAsyncHookCase(fixture,mode,isVoid,generated,"success");
    }
    public static IEnumerable<object[]> AsyncHookFailures()
    {
        yield return ["direct",false,"before-error"];yield return ["direct",true,"before-oce"];
        yield return ["owned",false,"before-null"];yield return ["async-owned",true,"pre-cancel"];
        yield return ["async-owned",false,"cancel-before"];yield return ["async-borrowed",true,"handler-error"];
        yield return ["direct",false,"handler-oce"];yield return ["owned",true,"after-error"];
        yield return ["async-owned",false,"after-oce"];yield return ["async-owned",true,"observer-error"];
        yield return ["owned",false,"observer-oce"];yield return ["owned",true,"finally-success-error"];
        yield return ["async-owned",false,"finally-success-oce"];yield return ["async-owned",true,"finally-body-error"];
        yield return ["owned",false,"cleanup-body-error"];yield return ["async-owned",true,"cleanup-finally-oce"];
        yield return ["two",false,"inner-before-error"];yield return ["two",true,"inner-after-oce"];
        yield return ["two",false,"inner-observer-error"];yield return ["two",true,"inner-finally-error"];
        yield return ["two",false,"outer-after-error"];yield return ["two",true,"outer-finally-error"];
        yield return ["borrowed",false,"factory-error"];yield return ["async-borrowed",true,"factory-oce"];yield return ["owned",false,"factory-null"];
    }
    [Theory][MemberData(nameof(AsyncHookFailures))]
    public async Task Async_hook_faults_cancellation_and_nested_exit_info_preserve_natural_exception_priority(string mode,bool isVoid,string scenario)
    {var f=HookAsyncFixture(mode,isVoid);foreach(var generated in new[]{false,true})await RunAsyncHookCase(f,mode,isVoid,generated,scenario);}
    [Theory]
    [InlineData("direct",false,"h0.before")] [InlineData("direct",true,"h0.after")]
    [InlineData("async-owned",false,"factory")] [InlineData("async-borrowed",true,"factory")]
    [InlineData("owned",false,"h0.cleanup")] [InlineData("async-owned",true,"h0.cleanup")]
    [InlineData("two",false,"h1.before")] [InlineData("two",true,"h0.finally")]
    [InlineData("borrowed",false,"handler")] [InlineData("owned",true,"h0.exception")]
    [InlineData("async-borrowed",false,"h0.after")] [InlineData("borrowed",true,"h0.finally")]
    public async Task Async_hook_single_real_suspension_consumes_callback_factory_or_cleanup_once(string mode,bool isVoid,string phase)
    {var f=HookAsyncFixture(mode,isVoid);foreach(var generated in new[]{false,true})await RunAsyncHookCase(f,mode,isVoid,generated,phase=="h0.exception"?"handler-error":"success",phase);}
    [Fact]
    public async Task Async_hook_pending_observer_OCE_overwrites_body_and_native_Before_keeps_its_context_boundary()
    {
        var f=HookAsyncFixture("direct",false);
        foreach(var generated in new[]{false,true})
        {await RunAsyncHookCase(f,"direct",false,generated,"observer-pending-oce","h0.exception");await RunAsyncHookCase(f,"direct",false,generated,"native-before");}
    }

    private static async Task RunAsyncHookCase(AsyncHookFixture f,string mode,bool isVoid,bool generated,string scenario,string? pending=null)
    {
        var p=new AsyncHookProbe{Pending=pending};var prior=AsyncHookProbe.Ambient.Value;
        using var foreign=new CancellationTokenSource();var body=new Exception("body");var before=new Exception("before");var after=new Exception("after");var observer=new Exception("observer");var final=new Exception("finally");var cleanup=new Exception("cleanup");var factory=new Exception("factory");var oce=new OperationCanceledException("OCE identity",foreign.Token);
        if(scenario.Contains("handler",StringComparison.Ordinal)||scenario is "observer-error" or "observer-oce" or "finally-body-error" or "cleanup-body-error" or "cleanup-finally-oce" or "inner-observer-error" or "inner-finally-error" or "observer-pending-oce")p.Failures["handler"]=scenario=="handler-oce"?oce:body;
        switch(scenario)
        {
            case "before-error":p.Failures["h0.before"]=before;break;case "before-oce":p.Failures["h0.before"]=oce;break;
            case "before-null":p.NullBefore=true;break;case "pre-cancel":p.Cancellation.Cancel();break;case "cancel-before":p.CancelPhase="h0.before";break;
            case "after-error":p.Failures["h0.after"]=after;break;case "after-oce":p.Failures["h0.after"]=oce;break;
            case "observer-error":p.Failures["h0.exception"]=observer;break;case "observer-oce":p.Failures["h0.exception"]=oce;break;
            case "finally-success-error":case "finally-body-error":p.Failures["h0.finally"]=final;break;case "finally-success-oce":p.Failures["h0.finally"]=oce;break;
            case "cleanup-body-error":p.Failures["h0.cleanup"]=cleanup;break;case "cleanup-finally-oce":p.Failures["h0.finally"]=final;p.Failures["h0.cleanup"]=oce;break;
            case "inner-before-error":p.Failures["h1.before"]=before;break;case "inner-after-oce":p.Failures["h1.after"]=oce;break;
            case "inner-observer-error":p.Failures["h1.exception"]=observer;break;case "inner-finally-error":p.Failures["h1.finally"]=final;break;
            case "outer-after-error":p.Failures["h0.after"]=after;break;case "outer-finally-error":p.Failures["h0.finally"]=final;break;
            case "factory-error":p.Failures["factory"]=factory;break;case "factory-oce":p.Failures["factory"]=oce;break;case "factory-null":p.NullFactory=true;break;
            case "native-before":p.NativePhase="h0.before";break;
        }
        try
        {
            IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);
            Assert.Contains(services,d=>d.ServiceType==f.Handler&&d.Lifetime==ServiceLifetime.Scoped);
            var hasFactory=mode is not ("direct" or "two");if(hasFactory){Assert.Contains(services,d=>d.ServiceType==f.Factory&&d.Lifetime==ServiceLifetime.Scoped);Assert.DoesNotContain(services,d=>d.ServiceType==f.Outer);}
            await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
            object request=Activator.CreateInstance(f.Request,[10])!;Exception? actual=null;
            await using(var scope=provider.CreateAsyncScope())
            {
                AsyncHookProbe.Ambient.Value="caller";Assert.Throws<ArgumentNullException>(()=>InvokeAsyncHook(f,mode,isVoid,generated,scope.ServiceProvider,null,default));Assert.Empty(p.Trace);
                var task=AsyncTask(InvokeAsyncHook(f,mode,isVoid,generated,scope.ServiceProvider,request,p.Cancellation.Token),isVoid);Assert.Equal("caller",AsyncHookProbe.Ambient.Value);
                if(pending!=null){Assert.False(task.IsCompleted);var gate=Assert.Single(p.Gates);Assert.Equal(0,gate.Consumptions);if(scenario=="observer-pending-oce")gate.Fail(oce);else gate.Complete();}
                try{await task.WaitAsync(TimeSpan.FromSeconds(10));}catch(Exception error){actual=error;}
                if(pending!=null)Assert.Equal(1,Assert.Single(p.Gates).Consumptions);
                var expected=ExpectedAsyncHookError(scenario,p,body,before,after,observer,final,cleanup,factory,oce);
                if(scenario=="before-null")Assert.IsType<ArgumentNullException>(actual);
                else if(scenario=="factory-null")Assert.IsType<InvalidOperationException>(actual);
                else if(scenario=="pre-cancel"){Assert.IsAssignableFrom<OperationCanceledException>(actual);Assert.Equal(p.Cancellation.Token,((OperationCanceledException)actual!).CancellationToken);}
                else Assert.Same(expected,actual);
                Assert.Equal(actual is OperationCanceledException,task.IsCanceled);Assert.Equal(actual!=null&&actual is not OperationCanceledException,task.IsFaulted);
                if(ReferenceEquals(actual,oce))Assert.Equal(foreign.Token,((OperationCanceledException)actual!).CancellationToken);
                if(actual==null&&!isVoid)Assert.Equal(mode=="two"?14:12,await (Task<int>)task);
                Assert.Equal(ExpectedAsyncHookTrace(mode,scenario),p.Trace);
                if(scenario=="native-before")Assert.Equal("caller",p.Calls.Single(c=>c.Phase=="handler").Ambient);
                var owned=mode.EndsWith("owned",StringComparison.Ordinal);Assert.Equal(owned&&p.Hooks.Count!=0?1:0,p.ReleasedHooks.Count);
                foreach(var call in p.Calls.Where(c=>c.Phase.Contains("exception",StringComparison.Ordinal)))Assert.NotNull(call.Error);
                if(scenario is "inner-observer-error" or "inner-finally-error")Assert.Same(scenario=="inner-observer-error"?observer:final,p.Calls.Single(c=>c.Phase=="h0.exception").Error);
                if(scenario is "observer-error" or "observer-oce" or "observer-pending-oce")Assert.Same(actual,p.Calls.Single(c=>c.Phase=="h0.finally").Exit!.Value.Error);
                foreach(var call in p.Calls.Where(c=>c.Exit!=null))
                {
                    var exit=call.Exit!.Value;var exceptionPhase=call.Phase.Replace("finally","exception",StringComparison.Ordinal);
                    if(exit.Success){Assert.Null(exit.Error);Assert.DoesNotContain(p.Calls,c=>c.Phase==exceptionPhase);}
                    else
                    {
                        var observed=p.Calls.Single(c=>c.Phase==exceptionPhase).Error;
                        var replacement=p.Failures.GetValueOrDefault(exceptionPhase)??(scenario=="observer-pending-oce"?oce:observed);
                        Assert.Same(replacement,exit.Error);
                    }
                }
                AssertStageRequestIdentities(p,request);
                WriteNestedHookEvidence("async-hook-observations",mode+"-"+isVoid+"-"+generated+"-"+scenario+"-"+(pending??"complete"),new{mode,isVoid,generated,scenario,pending,trace=p.Trace.ToArray(),task.IsCanceled,task.IsFaulted,escapingType=actual?.GetType().FullName,escapingIdentity=expected==null||ReferenceEquals(expected,actual),calls=p.Calls.Select(c=>new{c.Phase,requestValue=c.Request?.GetType().GetProperty("Value")!.GetValue(c.Request),error=c.Error?.Message,success=c.Exit?.Success,exitError=c.Exit?.Error?.Message,c.Ambient}),created=p.Hooks.Count,operationReleases=p.ReleasedHooks.Count,consumptions=p.Gates.Select(g=>g.Consumptions)});
                p.Pending=null;p.Failures.Clear();p.NativePhase=null;
            }
            if(mode is "borrowed" or "async-borrowed")foreach(var hook in p.Hooks)await ((IAsyncDisposable)hook).DisposeAsync();
            Assert.Equal(p.Hooks.Count,p.ReleasedHooks.Count);Assert.All(p.Hooks,h=>Assert.Single(p.ReleasedHooks,x=>ReferenceEquals(x,h)));
            Assert.Equal(p.Trace.Contains("handler.construct")?1:0,p.HandlerDisposals);Assert.Equal(p.Trace.Contains("factory.construct")?1:0,p.FactoryDisposals);
            var lifetime=ExpectedAsyncHookTrace(mode,scenario).ToList();if(p.HandlerDisposals!=0)lifetime.Add("handler.dispose");
            if(mode=="two"&&p.Hooks.Count==2)lifetime.Add("h1.cleanup");
            if(mode is "direct" or "two"){if(p.Hooks.Count!=0)lifetime.Add("h0.cleanup");}
            else if(p.FactoryDisposals!=0)lifetime.Add("factory.dispose");
            if(mode is "borrowed" or "async-borrowed"&&p.Hooks.Count!=0)lifetime.Add("h0.cleanup");
            Assert.Equal(lifetime,p.Trace);
            WriteNestedHookEvidence("async-hook-ownership",mode+"-"+isVoid+"-"+generated+"-"+scenario+"-"+(pending??"complete"),new{mode,isVoid,generated,scenario,pending,p.Trace,created=p.Hooks.Count,released=p.ReleasedHooks.Count,p.HandlerDisposals,p.FactoryDisposals});
        }
        finally{p.Cancellation.Dispose();AsyncHookProbe.Ambient.Value=prior;}
    }
    private static Exception? ExpectedAsyncHookError(string s,AsyncHookProbe p,Exception body,Exception before,Exception after,Exception observer,Exception final,Exception cleanup,Exception factory,Exception oce)=>s switch
    {
        "before-error" or "inner-before-error"=>before,"handler-error"=>body,"after-error" or "outer-after-error"=>after,
        "observer-error" or "inner-observer-error"=>observer,"finally-success-error" or "finally-body-error" or "inner-finally-error" or "outer-finally-error"=>final,
        "cleanup-body-error"=>cleanup,"factory-error"=>factory,
        "before-oce" or "handler-oce" or "after-oce" or "observer-oce" or "finally-success-oce" or "cleanup-finally-oce" or "inner-after-oce" or "factory-oce" or "observer-pending-oce"=>oce,
        "cancel-before"=>p.Calls.Single(c=>c.Phase=="h0.exception").Error,
        "pre-cancel"=>null,_=>null
    };
    private static string[] ExpectedAsyncHookTrace(string mode,string s)
    {
        if(s=="pre-cancel")return [];
        var factory=mode is not ("direct" or "two");var list=new List<string>();if(factory)list.AddRange(["factory.construct","factory"]);
        if(s.StartsWith("factory-",StringComparison.Ordinal))return list.ToArray();
        list.AddRange(["h0.construct","h0.before"]);
        if(s is "before-error" or "before-oce" or "before-null" or "cancel-before")list.AddRange(["h0.exception","h0.finally"]);
        else if(mode!="two")
        {
            list.AddRange(["handler.construct","handler"]);
            if(s is "handler-error" or "handler-oce" or "observer-error" or "observer-oce" or "observer-pending-oce" or "finally-body-error" or "cleanup-body-error" or "cleanup-finally-oce")list.Add("h0.exception");
            else{list.Add("h0.after");if(s is "after-error" or "after-oce")list.Add("h0.exception");}
            list.Add("h0.finally");
        }
        else
        {
            list.AddRange(["h1.construct","h1.before"]);
            if(s=="inner-before-error")list.AddRange(["h1.exception","h1.finally","h0.exception","h0.finally"]);
            else
            {
                list.AddRange(["handler.construct","handler"]);
                if(s is "inner-observer-error" or "inner-finally-error")list.AddRange(["h1.exception","h1.finally","h0.exception","h0.finally"]);
                else if(s=="inner-after-oce")list.AddRange(["h1.after","h1.exception","h1.finally","h0.exception","h0.finally"]);
                else{list.AddRange(["h1.after","h1.finally","h0.after"]);if(s=="outer-after-error")list.Add("h0.exception");list.Add("h0.finally");}
            }
        }
        if(mode.EndsWith("owned",StringComparison.Ordinal))list.Add("h0.cleanup");return list.ToArray();
    }
    private static void AssertStageRequestIdentities(AsyncHookProbe p,object original)
    {
        foreach(var id in new[]{0,1})
        {
            var before=p.Calls.SingleOrDefault(c=>c.Phase==$"h{id}.before");if(before.Phase==null)continue;
            if(id==0)Assert.Same(original,before.Request);
            var subsequent=p.Calls.Where(c=>c.Phase is not "handler"&&c.Phase.StartsWith($"h{id}.",StringComparison.Ordinal)&&c.Phase is not null&&c.Phase!=$"h{id}.before"&&c.Phase!=$"h{id}.cleanup").ToArray();
            if(subsequent.Length!=0)Assert.All(subsequent,c=>Assert.Same(subsequent[0].Request,c.Request));
            if(p.Failures.ContainsKey($"h{id}.before"))Assert.All(subsequent,c=>Assert.Same(before.Request,c.Request));
            else if(!p.NullBefore)Assert.All(subsequent,c=>{Assert.NotSame(before.Request,c.Request);Assert.Equal((int)before.Request!.GetType().GetProperty("Value")!.GetValue(before.Request)!+1,(int)c.Request!.GetType().GetProperty("Value")!.GetValue(c.Request)!);});
        }
        var handler=p.Calls.SingleOrDefault(c=>c.Phase=="handler");
        if(handler.Phase!=null)
        {
            var innermost=p.Calls.Any(c=>c.Phase=="h1.before")?1:0;
            Assert.Same(p.Calls.Single(c=>c.Phase==$"h{innermost}.finally").Request,handler.Request);
        }
    }

    [Theory][InlineData("partial-wrapper")][InlineData("sync-mixed-wrapper")][InlineData("metadata-third")][InlineData("metadata-second-factory")]
    [InlineData("metadata")][InlineData("metadata-wrapper-session")][InlineData("stream")]
    public void Async_hook_unsupported_routes_remain_diagnosed_without_execution(string shape)
    {
        var source=AsyncHookSource(shape is "metadata-second-factory" or "metadata-wrapper-session"?"owned":"direct",false);
        var start=source.IndexOf("public static object Dispatch",StringComparison.Ordinal);source=source.Remove(start,source.IndexOf(';',start)-start+1);
        source=shape switch
        {
            "partial-wrapper"=>source.Replace("IAsyncSendFinallyObserver<Ping>,","").Replace("ValueTask IAsyncSendFinallyObserver<Ping>.FinallyAsync(Ping r,SendExitInfo e,CancellationToken t){_probe.CheckToken(t);return _probe.Finally(_id,r,e);}","").Replace("Stage.Hook<Hook<OuterTag>>()","Stage.Hook<Hook<OuterTag>>(),Stage.Wrapper<global::Zendiator.Generator.Tests.MetadataWrapper<Ping>>()")+"namespace App{public sealed class W:IAsyncSendWrapper<Ping,int>{public ValueTask<int> InvokeAsync<TNext>(Ping r,TNext n,CancellationToken t)where TNext:IAsyncSendContinuation<Ping,int> =>n.InvokeAsync(r,t);}}",
            "sync-mixed-wrapper"=>source.Replace("IAsyncSendFinallyObserver<Ping>","ISendFinallyObserver<Ping>").Replace("ValueTask ISendFinallyObserver<Ping>.FinallyAsync(","void ISendFinallyObserver<Ping>.Finally(").Replace("return _probe.Finally(_id,r,e);","_ = _probe.Finally(_id,r,e);").Replace("Stage.Hook<Hook<OuterTag>>()","Stage.Hook<Hook<OuterTag>>(),Stage.Wrapper<global::Zendiator.Generator.Tests.MetadataWrapper<Ping>>()")+"namespace App{public sealed class W:IAsyncSendWrapper<Ping,int>{public ValueTask<int> InvokeAsync<TNext>(Ping r,TNext n,CancellationToken t)where TNext:IAsyncSendContinuation<Ping,int> =>n.InvokeAsync(r,t);}}",
            "metadata-third"=>source.Replace("Stage.Hook<Hook<OuterTag>>()","Stage.Hook<Hook<OuterTag>>(),Stage.Hook<Hook<InnerTag>>(),Stage.Hook<global::Zendiator.Generator.Tests.MetadataAsyncHook<Ping>>()"),
            "metadata-second-factory"=>source.Replace("Stage.Hook<Hook<OuterTag>,Factory<OuterTag>>()","Stage.Hook<Hook<OuterTag>,Factory<OuterTag>>(),Stage.Hook<Hook<InnerTag>,global::Zendiator.Generator.Tests.MetadataOwnedReceiverFactory<Ping,Hook<InnerTag>>>()"),
            "metadata"=>source.Replace("Stage.Hook<Hook<OuterTag>>()","Stage.Hook<global::Zendiator.Generator.Tests.MetadataAsyncHook<Ping>>()"),
            "metadata-wrapper-session"=>source.Replace("Stage.Hook<Hook<OuterTag>,Factory<OuterTag>>()","Stage.Wrapper<Wrap,global::Zendiator.Generator.Tests.MetadataOwnedReceiverFactory<Ping,Wrap>>()")+"namespace App {public sealed class Wrap:IAsyncSendWrapper<Ping,int>,IAsyncDisposable{public ValueTask<int> InvokeAsync<TNext>(Ping r,TNext next,CancellationToken t) where TNext:IAsyncSendContinuation<Ping,int> =>next.InvokeAsync(r,t);public ValueTask DisposeAsync()=>default;}}",
            _=>"using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Runtime.CompilerServices;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;namespace App {public sealed record Ping:IStreamRequest<int>;[ZendiatorHandler]public sealed class Handler:IStreamRequestHandler<Ping,int>{public async IAsyncEnumerable<int> HandleAsync(Ping r,[EnumeratorCancellation]CancellationToken t){await Task.CompletedTask;yield return 1;}}[ZendiatorComposition(\"stream\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>c.Handlers<Ping>(DispatchFamily.Stream,typeof(Handler)));}}"
        };
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"HookUnsupported"+shape),out var output,out var diagnostics);
        Assert.Equal("ZEN0037",Assert.Single(diagnostics).Id);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName.StartsWith("Zendiator.Canonical",StringComparison.Ordinal));
        WriteNestedHookEvidence("async-hook-boundaries",shape,new{shape,source,diagnostics=diagnostics.Select(d=>new{d.Id,message=d.GetMessage()}),generatedExecution=false});
    }
    [Fact]
    public void Async_hook_body_edits_cache_emission_and_factory_ownership_changes_it()
    {
        var source=AsyncHookSource("async-owned",false);var driver=SyncDriver().RunGenerators(SyncCompilation(source,"HookIncremental"));
        var body=driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.SendAsync.g.cs").SourceText.ToString();
        driver=driver.RunGenerators(SyncCompilation(source.Replace("new Ping(r.Value+1)","new Ping(r.Value+2)"),"HookIncremental"));
        Assert.Equal(body,driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.SendAsync.g.cs").SourceText.ToString());
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalAsyncSourceEmission"].SelectMany(s=>s.Outputs),o=>Assert.Equal(IncrementalStepRunReason.Cached,o.Reason));
        driver=driver.RunGenerators(SyncCompilation(source.Replace("IAsyncOwnedSessionFactory","IAsyncBorrowedSessionFactory"),"HookIncremental"));
        Assert.NotEqual(body,driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName=="Zendiator.Canonical.SendAsync.g.cs").SourceText.ToString());
        WriteNestedHookEvidence("async-hook-incremental","body-and-ownership",new{bodyCached=true,ownershipChangesEmission=true});
    }
    [Theory][InlineData("direct",true)][InlineData("async-owned",false)]
    public void Async_hook_emitter_preserves_accepted_zero_Hook_generation_only(string mode,bool isVoid)=>AsyncRuntimeFixture(mode,isVoid);
}
