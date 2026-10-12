using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    public sealed class MixedSessionProbe
    {
        public readonly HookHandlerSessionProbe Handler=new();
        public ReferenceSendProbe Core=>Handler.Core;
        public Exception? HookDiError,HookFactoryConstructorError,HookFactoryError,HookPartialError,HookPartialCleanupError,HookCleanupError,HandlerPartialError,HandlerPartialCleanupError;
        public bool NullHookFactory,NullHookSession,CancelHookFactory,CancelHookCleanup;
        public int HookDependencies,HookDependencyDisposals,HookFactoryDisposals,HookPartialDisposals;
        public readonly List<object> CreatedHooks=new(),ReleasedHooks=new(),CreatedPartials=new(),ReleasedPartials=new();
        public readonly List<IDisposable> BorrowedHooks=new();
        public readonly List<(string Phase,object Request,CancellationToken Token,string? Ambient,string Culture)> HookCalls=new(),PartialCalls=new();
        public static string CultureFor(string phase)=>phase switch{"caller"=>"","hook.factory" or "factory"=>"it-IT","hook.dispose"=>"sv-SE","handler.dispose"=>"ko-KR","partial.dispose" or "hook.partial.dispose"=>"pl-PL",_=>ReferenceSendProbe.CultureFor(phase)};
        private void Record(string phase,object request,CancellationToken token,bool partial=false)
        {
            Core.Phases.Add(phase);(partial?PartialCalls:HookCalls).Add((phase,request,token,ReferenceSendProbe.Ambient.Value,CultureInfo.CurrentCulture.Name));
            ReferenceSendProbe.Ambient.Value=phase;CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(CultureFor(phase));
        }
        public H AcquireHook<H>(object request,CancellationToken token,Func<H> create,bool borrowed) where H:class,IDisposable
        {
            Record("hook.factory",request,token);if(CancelHookFactory)Core.Cancellation.Cancel();if(HookFactoryError is {} error)throw error;
            if(HookPartialError is {} partial)
            {
                var resource=new MixedPartialResource(this,true,request,token);
                try{throw partial;}
                finally{resource.Dispose();}
            }
            if(NullHookSession)return null!;var hook=create();CreatedHooks.Add(hook);if(borrowed)BorrowedHooks.Add(hook);return hook;
        }
        public H BuildHandler<H>(object request,CancellationToken token,Func<H> create)
        {
            if(HandlerPartialError is {} partial)
            {
                var resource=new MixedPartialResource(this,false,request,token);
                try{throw partial;}
                finally{resource.Dispose();}
            }
            return create();
        }
        public void ReleaseHook(object hook,object request,CancellationToken token)
        {Record("hook.dispose",request,token);ReleasedHooks.Add(hook);Core.HookDisposals++;if(CancelHookCleanup)Core.Cancellation.Cancel();if(HookCleanupError is {} error)throw error;}
        public void ReleasePartial(object resource,bool hook,object request,CancellationToken token)
        {
            Record(hook?"hook.partial.dispose":"partial.dispose",request,token,true);ReleasedPartials.Add(resource);
            if(hook)HookPartialDisposals++;else Handler.PartialDisposals++;
            if((hook?HookPartialCleanupError:HandlerPartialCleanupError) is {} error)throw error;
        }
    }
    public sealed class MixedPartialResource : IDisposable
    {
        private readonly MixedSessionProbe _probe;private readonly bool _hook;private readonly object _request;private readonly CancellationToken _token;
        public MixedPartialResource(MixedSessionProbe probe,bool hook,object request,CancellationToken token)
        {_probe=probe;_hook=hook;_request=request;_token=token;probe.Core.Phases.Add(hook?"hook.partial.acquire":"partial.acquire");probe.CreatedPartials.Add(this);}
        public void Dispose()=>_probe.ReleasePartial(this,_hook,_request,_token);
    }
    public sealed class MixedHookDependency : IDisposable
    {
        private readonly MixedSessionProbe _probe;
        public MixedHookDependency(MixedSessionProbe probe){_probe=probe;probe.Core.Phases.Add("hook-dependency.ctor");probe.HookDependencies++;}
        public void Dispose(){_probe.Core.Phases.Add("hook-dependency.dispose");_probe.HookDependencyDisposals++;}
    }
    public abstract class MixedHookFactory<R,H> : IDisposable where R:class where H:class,IDisposable
    {
        private readonly MixedSessionProbe _probe;private readonly Func<R,CancellationToken,H> _create;
        protected MixedHookFactory(MixedSessionProbe probe,MixedHookDependency dependency,Func<R,CancellationToken,H> create)
        {_probe=probe;_create=create;probe.Core.Phases.Add("hook-factory.ctor");if(probe.HookFactoryConstructorError is {} error)throw error;}
        protected H Create(R request,CancellationToken token,bool borrowed)=>_probe.AcquireHook(request,token,()=>_create(request,token),borrowed);
        public void Dispose(){_probe.Core.Phases.Add("hook-factory.dispose");_probe.HookFactoryDisposals++;}
    }
    public sealed class OwnedMixedHookFactory<R,H>(MixedSessionProbe probe,MixedHookDependency dependency,Func<R,CancellationToken,H> create)
        :MixedHookFactory<R,H>(probe,dependency,create),IOwnedSessionFactory<R,H> where R:class where H:class,IDisposable
    {public H Create(R request,CancellationToken token)=>Create(request,token,false);}
    public sealed class BorrowedMixedHookFactory<R,H>(MixedSessionProbe probe,MixedHookDependency dependency,Func<R,CancellationToken,H> create)
        :MixedHookFactory<R,H>(probe,dependency,create),IBorrowedSessionFactory<R,H> where R:class where H:class,IDisposable
    {public H Create(R request,CancellationToken token)=>Create(request,token,true);}
    public sealed class MixedResponseHookSession : ISendRequestTransform<ReferenceResponseRequest>,ISendResponseTransform<ReferenceResponseRequest,int>,ISendExceptionObserver<ReferenceResponseRequest>,ISendFinallyObserver<ReferenceResponseRequest>,IDisposable
    {
        private readonly MixedSessionProbe _probe;private readonly ReferenceResponseRequest _request;private readonly CancellationToken _token;
        public MixedResponseHookSession(MixedSessionProbe probe,ReferenceResponseRequest request,CancellationToken token){_probe=probe;_request=request;_token=token;probe.Core.ConstructHook();}
        public ReferenceResponseRequest Before(ReferenceResponseRequest request,CancellationToken token)=>(ReferenceResponseRequest)_probe.Core.Before(request,token)!;
        public int After(ReferenceResponseRequest request,int response,CancellationToken token)=>_probe.Core.After(request,response,token);
        public void OnException(ReferenceResponseRequest request,Exception error,CancellationToken token)=>_probe.Core.Observe(request,error,token);
        public void Finally(ReferenceResponseRequest request,SendExitInfo exit,CancellationToken token)=>_probe.Core.Finish(request,exit,token);
        public void Dispose()=>_probe.ReleaseHook(this,_request,_token);
    }
    public sealed class MixedVoidHookSession : ISendRequestTransform<ReferenceVoidRequest>,ISendSuccessObserver<ReferenceVoidRequest>,ISendExceptionObserver<ReferenceVoidRequest>,ISendFinallyObserver<ReferenceVoidRequest>,IDisposable
    {
        private readonly MixedSessionProbe _probe;private readonly ReferenceVoidRequest _request;private readonly CancellationToken _token;
        public MixedVoidHookSession(MixedSessionProbe probe,ReferenceVoidRequest request,CancellationToken token){_probe=probe;_request=request;_token=token;probe.Core.ConstructHook();}
        public ReferenceVoidRequest Before(ReferenceVoidRequest request,CancellationToken token)=>(ReferenceVoidRequest)_probe.Core.Before(request,token)!;
        public void After(ReferenceVoidRequest request,CancellationToken token)=>_probe.Core.After(request,0,token);
        public void OnException(ReferenceVoidRequest request,Exception error,CancellationToken token)=>_probe.Core.Observe(request,error,token);
        public void Finally(ReferenceVoidRequest request,SendExitInfo exit,CancellationToken token)=>_probe.Core.Finish(request,exit,token);
        public void Dispose()=>_probe.ReleaseHook(this,_request,_token);
    }
    private static (Type Hook,Type Handler,Type HookFactory,Type HandlerFactory,Type Request) MixedSessionTypes(bool hookOwned,bool handlerOwned,bool isVoid,SyncFixture? fixture=null)
    {
        if(fixture is {} f)return(f.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string)),f.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string)),f.Assembly.GetType("App.HookFactory`1")!.MakeGenericType(typeof(string)),f.Service,f.Request);
        var request=isVoid?typeof(ReferenceVoidRequest):typeof(ReferenceResponseRequest);
        var hook=isVoid?typeof(MixedVoidHookSession):typeof(MixedResponseHookSession);var handler=isVoid?typeof(VoidHookHandlerSession):typeof(ResponseHookHandlerSession);
        return(hook,handler,(hookOwned?typeof(OwnedMixedHookFactory<,>):typeof(BorrowedMixedHookFactory<,>)).MakeGenericType(request,hook),
            (handlerOwned?typeof(OwnedHookHandlerFactory<,>):typeof(BorrowedHookHandlerFactory<,>)).MakeGenericType(request,handler),request);
    }
    private static IServiceCollection MixedSessionServices(MixedSessionProbe mixed,bool hookOwned,bool handlerOwned,bool isVoid,ServiceLifetime lifetime,SyncFixture? fixture=null)
    {
        var probe=mixed.Handler;var types=MixedSessionTypes(hookOwned,handlerOwned,isVoid,fixture);IServiceCollection services=new ServiceCollection();
        services.AddSingleton(mixed);services.AddSingleton(probe);services.AddSingleton(mixed.Core);
        services.Add(ServiceDescriptor.Describe(typeof(MixedHookDependency),typeof(MixedHookDependency),lifetime));
        services.Add(ServiceDescriptor.Describe(typeof(HookHandlerFactoryDependency),typeof(HookHandlerFactoryDependency),lifetime));
        if(fixture is {} f)
        {
            var hookResolver=f.Assembly.GetType("App.Bridge")!.GetMethod("ResolveHookFactory")!.CreateDelegate<Func<IServiceProvider,object?>>();
            var handlerResolver=f.ResolveService.CreateDelegate<Func<IServiceProvider,object?>>();
            services.Add(ServiceDescriptor.Describe(types.HookFactory,p=>hookResolver(p)!,lifetime));
            services.Add(ServiceDescriptor.Describe(types.HandlerFactory,p=>handlerResolver(p)!,lifetime));
            services.AddScoped(types.Hook,_=>throw new Exception("Hook session must come from its factory."));services.AddScoped(types.Handler,_=>throw new Exception("Handler session must come from its factory."));
            f.Register.Invoke(null,[services]);return services;
        }
        services.Add(ServiceDescriptor.Describe(types.HookFactory,provider=>{
            mixed.Core.Phases.Add("resolve-hook-factory");if(mixed.HookDiError is {} error)throw error;if(mixed.NullHookFactory)return null!;
            var dependency=provider.GetRequiredService<MixedHookDependency>();
            return isVoid?hookOwned?new OwnedMixedHookFactory<ReferenceVoidRequest,MixedVoidHookSession>(mixed,dependency,(r,t)=>new(mixed,r,t)):
                new BorrowedMixedHookFactory<ReferenceVoidRequest,MixedVoidHookSession>(mixed,dependency,(r,t)=>new(mixed,r,t)):
                hookOwned?new OwnedMixedHookFactory<ReferenceResponseRequest,MixedResponseHookSession>(mixed,dependency,(r,t)=>new(mixed,r,t)):
                new BorrowedMixedHookFactory<ReferenceResponseRequest,MixedResponseHookSession>(mixed,dependency,(r,t)=>new(mixed,r,t));
        },lifetime));
        services.Add(ServiceDescriptor.Describe(types.HandlerFactory,provider=>{
            mixed.Core.Phases.Add("resolve-factory");if(probe.DiError is {} error)throw error;if(probe.NullFactory)return null!;
            var dependency=provider.GetRequiredService<HookHandlerFactoryDependency>();
            return isVoid?handlerOwned?new OwnedHookHandlerFactory<ReferenceVoidRequest,VoidHookHandlerSession>(probe,dependency,(r,t)=>mixed.BuildHandler(r,t,()=>new VoidHookHandlerSession(probe,r,t))):
                new BorrowedHookHandlerFactory<ReferenceVoidRequest,VoidHookHandlerSession>(probe,dependency,(r,t)=>mixed.BuildHandler(r,t,()=>new VoidHookHandlerSession(probe,r,t))):
                handlerOwned?new OwnedHookHandlerFactory<ReferenceResponseRequest,ResponseHookHandlerSession>(probe,dependency,(r,t)=>mixed.BuildHandler(r,t,()=>new ResponseHookHandlerSession(probe,r,t))):
                new BorrowedHookHandlerFactory<ReferenceResponseRequest,ResponseHookHandlerSession>(probe,dependency,(r,t)=>mixed.BuildHandler(r,t,()=>new ResponseHookHandlerSession(probe,r,t)));
        },lifetime));
        services.AddScoped(types.Hook,_=>throw new Exception("Hook session must come from its factory."));services.AddScoped(types.Handler,_=>throw new Exception("Handler session must come from its factory."));return services;
    }
    private static (object? Result,Exception? Error) InvokeMixedSessionReference(IServiceProvider provider,bool hookOwned,bool handlerOwned,bool isVoid,object? request,CancellationToken token,SyncFixture? fixture=null)
    {
        var types=MixedSessionTypes(hookOwned,handlerOwned,isVoid,fixture);
        var name="Send"+(hookOwned?"Owned":"Borrowed")+"Hook"+(handlerOwned?"Owned":"Borrowed")+"Handler"+(isVoid?"Void":"Response");
        var method=typeof(CanonicalSendSyncReference).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!
            .MakeGenericMethod(isVoid?[types.Request,types.Hook,types.Handler,types.HookFactory,types.HandlerFactory]:[types.Request,typeof(int),types.Hook,types.Handler,types.HookFactory,types.HandlerFactory]);
        try{return(method.Invoke(null,[provider,request,token]),null);}catch(TargetInvocationException error){return(null,error.InnerException);}
    }
    public static IEnumerable<object[]> MixedSessionReferenceCases()
    {
        const string hookPrefix="resolve-hook-factory hook-dependency.ctor hook-factory.ctor hook.factory hook.ctor";
        const string hookFactory="resolve-hook-factory hook-dependency.ctor hook-factory.ctor hook.factory";
        foreach(var data in HookHandlerReferenceCases())foreach(var hookOwned in new[]{false,true})
        {
            var golden=((string)data[3]).Replace("hook.ctor",hookPrefix).Replace("stage.resolve","resolve-hook-factory");
            if(hookOwned&&golden.Split(' ').Contains("before"))golden+=" hook.dispose";
            yield return[hookOwned,data[0],data[1],data[2],golden];
        }
        foreach(var hookOwned in new[]{false,true})foreach(var handlerOwned in new[]{false,true})foreach(var isVoid in new[]{false,true})
        {
            foreach(var scenario in new[]{"hook-di-null","hook-factory-ctor-error","hook-factory-error","hook-factory-oce","hook-factory-null","hook-partial-error","hook-partial-oce","hook-partial-cleanup-error","cancel-in-hook-factory"})
            {
                var golden=scenario switch{"hook-di-null"=>"resolve-hook-factory","hook-factory-ctor-error"=>"resolve-hook-factory hook-dependency.ctor hook-factory.ctor",
                    "hook-partial-error" or "hook-partial-oce" or "hook-partial-cleanup-error"=>hookFactory+" hook.partial.acquire hook.partial.dispose",
                    "cancel-in-hook-factory"=>hookPrefix+" before onexception finally"+(hookOwned?" hook.dispose":""),_=>hookFactory};
                yield return[hookOwned,handlerOwned,isVoid,scenario,golden];
            }
            foreach(var scenario in new[]{"handler-partial-cleanup-error","handler-partial-cleanup-oce"})
                yield return[hookOwned,handlerOwned,isVoid,scenario,hookPrefix+" before resolve-factory dependency.ctor factory.ctor factory partial.acquire partial.dispose onexception finally"+(hookOwned?" hook.dispose":"")];
        }
        foreach(var handlerOwned in new[]{false,true})foreach(var isVoid in new[]{false,true})foreach(var scenario in new[]{"hook-cleanup-error-success","hook-cleanup-oce-success","hook-cleanup-error-over-body","hook-cleanup-oce-over-body","cancel-in-hook-cleanup"})
            yield return[true,handlerOwned,isVoid,scenario,hookPrefix+" before resolve-factory dependency.ctor factory.ctor factory handler.ctor handler "+(handlerOwned?"handler.dispose ":"")+(scenario.EndsWith("over-body",StringComparison.Ordinal)?"onexception finally":"after finally")+" hook.dispose"];
        foreach(var isVoid in new[]{false,true})
        {
            foreach(var inner in new[]{"error","oce"})foreach(var outer in new[]{"error","oce"})
                yield return[true,true,isVoid,"two-cleanups-"+inner+"-"+outer,hookPrefix+" before resolve-factory dependency.ctor factory.ctor factory handler.ctor handler handler.dispose onexception finally hook.dispose"];
            foreach(var scenario in new[]{"all-cleanups-error","all-cleanups-oce"})
                yield return[true,true,isVoid,scenario,hookPrefix+" before resolve-factory dependency.ctor factory.ctor factory handler.ctor handler handler.dispose onexception finally hook.dispose"];
        }
    }
    [Theory]
    [MemberData(nameof(MixedSessionReferenceCases))]
    public void Canonical_mixed_sessions_preserve_nested_cleanup_observation_and_last_error(bool hookOwned,bool owned,bool isVoid,string scenario,string phases)
        =>_ = RunMixedSessionScenario(hookOwned,owned,isVoid,scenario,phases);
    private static string RunMixedSessionScenario(bool hookOwned,bool owned,bool isVoid,string scenario,string phases,SyncFixture? fixture=null,bool generated=false)
    {
        object? operation=null;
        var mixed=new MixedSessionProbe();var probe=mixed.Handler;var core=mixed.Core;
        var ambient=ReferenceSendProbe.Ambient.Value;var culture=CultureInfo.CurrentCulture;
        ReferenceSendProbe.Ambient.Value="caller";CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        using var otherToken=new CancellationTokenSource();var token=core.Cancellation.Token;
        Exception body=scenario is "handler-oce" or "after-oce" or "cleanup-error-over-oce" or "cleanup-oce-over-oce"?new OperationCanceledException(otherToken.Token):new Exception("body identity");
        var observer=new Exception("observer identity");Exception final=scenario is "finally-oce-over-body" or "finally-oce-over-cleanup"?new OperationCanceledException(otherToken.Token):new Exception("Finally identity");
        Exception cleanup=scenario.StartsWith("cleanup-oce",StringComparison.Ordinal)?new OperationCanceledException(otherToken.Token):new Exception("cleanup identity");
        Exception factory=scenario is "factory-oce" or "partial-oce"?new OperationCanceledException(otherToken.Token):new Exception("factory identity");
        object? request=scenario=="null-request"?null:fixture!=null?Activator.CreateInstance(fixture.Request):isVoid?new ReferenceVoidRequest():new ReferenceResponseRequest();object replacement=fixture!=null?Activator.CreateInstance(fixture.Request)!:isVoid?new ReferenceVoidRequest():new ReferenceResponseRequest();
        core.TransformRequest=scenario is "replace-request" or "before-null" or "before-error";core.Replacement=scenario=="before-null"?null:replacement;
        core.BeforeError=scenario=="before-error"?body:null;core.StageConstructorError=scenario=="stage-ctor-error"?body:null;core.HandlerConstructorError=scenario=="handler-ctor-error"?body:null;
        core.HandlerError=scenario is "handler-error" or "handler-oce" or "observer-error-over-body" or "finally-error-over-body" or "finally-oce-over-body" or "finally-error-over-observer" or "cleanup-error-over-body" or "cleanup-oce-over-body" or "cleanup-error-over-oce" or "cleanup-oce-over-oce"?body:null;
        core.AfterError=scenario is "after-error" or "after-oce"?body:null;core.ObserverError=scenario is "observer-error-over-body" or "finally-error-over-observer" or "observer-error-over-cleanup"?observer:null;
        core.FinallyError=scenario.StartsWith("finally-",StringComparison.Ordinal)?final:null;
        probe.CleanupError=scenario.StartsWith("cleanup-",StringComparison.Ordinal)||scenario.EndsWith("over-cleanup",StringComparison.Ordinal)?cleanup:null;
        probe.DiError=scenario=="handler-di-error"?body:null;probe.NullFactory=scenario=="factory-di-null";probe.FactoryConstructorError=scenario=="factory-ctor-error"?factory:null;
        probe.FactoryError=scenario is "factory-error" or "factory-oce"?factory:null;probe.PartialError=scenario is "partial-error" or "partial-oce"?factory:null;probe.NullSession=scenario=="factory-null";
        core.CancelBefore=scenario=="cancel-in-before";core.CancelHandler=scenario=="cancel-in-handler";core.CancelAfter=scenario=="cancel-in-after";core.CancelFinally=scenario=="cancel-in-finally";
        probe.CancelFactory=scenario=="cancel-in-factory";probe.CancelCleanup=scenario=="cancel-in-cleanup";if(scenario=="pre-cancel")core.Cancellation.Cancel();

        mixed.HandlerPartialError=probe.PartialError;probe.PartialError=null;
        mixed.HookDiError=scenario=="stage-di-error"?body:null;mixed.NullHookFactory=scenario=="hook-di-null";
        Exception hookFactory=scenario is "hook-factory-oce" or "hook-partial-oce"?new OperationCanceledException(otherToken.Token):new Exception("Hook factory identity");
        mixed.HookFactoryConstructorError=scenario=="hook-factory-ctor-error"?hookFactory:null;
        mixed.HookFactoryError=scenario is "hook-factory-error" or "hook-factory-oce"?hookFactory:null;
        mixed.HookPartialError=scenario is "hook-partial-error" or "hook-partial-oce" or "hook-partial-cleanup-error"?hookFactory:null;
        mixed.HookPartialCleanupError=scenario=="hook-partial-cleanup-error"?new Exception("Hook partial cleanup identity"):null;
        mixed.NullHookSession=scenario=="hook-factory-null";mixed.CancelHookFactory=scenario=="cancel-in-hook-factory";mixed.CancelHookCleanup=scenario=="cancel-in-hook-cleanup";
        if(scenario.StartsWith("handler-partial-cleanup-",StringComparison.Ordinal))
        {mixed.HandlerPartialError=factory;mixed.HandlerPartialCleanupError=scenario.EndsWith("oce",StringComparison.Ordinal)?new OperationCanceledException(otherToken.Token):new Exception("Handler partial cleanup identity");}
        Exception hookCleanup=scenario.Contains("hook-cleanup-oce",StringComparison.Ordinal)||scenario.StartsWith("two-cleanups-",StringComparison.Ordinal)&&scenario.EndsWith("oce",StringComparison.Ordinal)||scenario=="all-cleanups-oce"?new OperationCanceledException(otherToken.Token):new Exception("Hook cleanup identity");
        if(scenario.StartsWith("hook-cleanup-",StringComparison.Ordinal))mixed.HookCleanupError=hookCleanup;
        if(scenario.StartsWith("hook-cleanup-",StringComparison.Ordinal)&&scenario.EndsWith("over-body",StringComparison.Ordinal))core.HandlerError=body;
        if(scenario.StartsWith("two-cleanups-",StringComparison.Ordinal)||scenario.StartsWith("all-cleanups-",StringComparison.Ordinal))
        {
            core.HandlerError=body;probe.CleanupError=scenario.StartsWith("two-cleanups-oce-",StringComparison.Ordinal)||scenario=="all-cleanups-oce"?new OperationCanceledException(otherToken.Token):cleanup;
            mixed.HookCleanupError=hookCleanup;
            if(scenario.StartsWith("all-cleanups-",StringComparison.Ordinal)){core.ObserverError=observer;core.FinallyError=scenario=="all-cleanups-oce"?new OperationCanceledException(otherToken.Token):final;}
        }
        var golden=phases.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var expectedInner=probe.CleanupError??probe.DiError??probe.FactoryConstructorError??probe.FactoryError??mixed.HandlerPartialCleanupError??mixed.HandlerPartialError??core.HandlerConstructorError??core.HandlerError;
        var acquisitionError=mixed.HookDiError??mixed.HookFactoryConstructorError??mixed.HookFactoryError??mixed.HookPartialCleanupError??mixed.HookPartialError??core.StageConstructorError;
        var expectedEscaping=golden.Contains("before")?(hookOwned?mixed.HookCleanupError:null)??core.FinallyError??core.ObserverError??expectedInner??core.BeforeError??core.AfterError:acquisitionError;
        var knownObserved=golden.Contains("onexception")?expectedInner??core.BeforeError??core.AfterError:null;
        try
        {
            using(var provider=MixedSessionServices(mixed,hookOwned,owned,isVoid,ServiceLifetime.Scoped,fixture).BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true}))
            {
                using(var scope=provider.CreateScope())
                {
                    var actual=fixture!=null&&generated?InvokeSync(fixture,scope.ServiceProvider.GetRequiredService(fixture.Interface),request,token):InvokeMixedSessionReference(scope.ServiceProvider,hookOwned,owned,isVoid,request,token,fixture);Assert.Equal(golden,core.Phases);
                    if(expectedEscaping!=null)Assert.Same(expectedEscaping,actual.Error);
                    else if(scenario is "null-request" or "before-null")Assert.Equal("request",Assert.IsType<ArgumentNullException>(actual.Error).ParamName);
                    else if(scenario is "pre-cancel" or "cancel-in-before" or "cancel-in-hook-factory")Assert.Equal(token,Assert.IsType<OperationCanceledException>(actual.Error).CancellationToken);
                    else if(scenario is "factory-null" or "factory-di-null" or "hook-factory-null" or "hook-di-null")Assert.IsType<InvalidOperationException>(actual.Error);
                    else{Assert.Null(actual.Error);Assert.Equal(isVoid?null:(object)42,actual.Result);}
                    var expectedObserved=golden.Contains("onexception")?knownObserved??actual.Error:null;Assert.Same(expectedObserved,core.ObservedError);
                    if(golden.Contains("finally")){var exit=Assert.IsType<SendExitInfo>(core.Exit);Assert.Equal(!golden.Contains("onexception"),exit.Success);Assert.Same(core.ObserverError??expectedObserved,exit.Error);}else Assert.Null(core.Exit);
                    var saved=scenario=="before-null"?null:scenario=="replace-request"?replacement:request;
                    var expectedContexts=new Dictionary<string,string>();var current="caller";
                    foreach(var phase in golden)
                        if(phase is "hook.factory" or "before" or "factory" or "partial.dispose" or "hook.partial.dispose" or "handler" or "handler.dispose" or "after" or "onexception" or "finally" or "hook.dispose")
                        {expectedContexts.Add(phase,current);current=phase;}
                    foreach(var call in core.Calls)
                    {Assert.Same(call.Phase=="before"?request:saved,call.Request);Assert.Equal(token,call.Token);Assert.Equal(expectedContexts[call.Phase],call.Ambient);Assert.Equal(MixedSessionProbe.CultureFor(expectedContexts[call.Phase]),call.Culture);}
                    foreach(var call in probe.Calls)
                    {Assert.Same(saved,call.Request);Assert.Equal(token,call.Token);Assert.Equal(expectedContexts[call.Phase],call.Ambient);Assert.Equal(MixedSessionProbe.CultureFor(expectedContexts[call.Phase]),call.Culture);}
                    foreach(var call in mixed.HookCalls)
                    {Assert.Same(request,call.Request);Assert.Equal(token,call.Token);Assert.Equal(expectedContexts[call.Phase],call.Ambient);Assert.Equal(MixedSessionProbe.CultureFor(expectedContexts[call.Phase]),call.Culture);}
                    foreach(var call in mixed.PartialCalls)
                    {Assert.Same(call.Phase=="hook.partial.dispose"?request:saved,call.Request);Assert.Equal(token,call.Token);Assert.Equal(expectedContexts[call.Phase],call.Ambient);Assert.Equal(MixedSessionProbe.CultureFor(expectedContexts[call.Phase]),call.Culture);}
                    Assert.Equal(current,ReferenceSendProbe.Ambient.Value);Assert.Equal(MixedSessionProbe.CultureFor(current),CultureInfo.CurrentCulture.Name);
                    var hooks=golden.Contains("before")?1:0;var handlers=golden.Contains("handler")?1:0;
                    Assert.Equal(hooks,mixed.CreatedHooks.Count);Assert.Equal(handlers,probe.Created.Count);
                    Assert.Equal(hookOwned?hooks:0,mixed.ReleasedHooks.Count);Assert.Equal(owned?handlers:0,probe.Released.Count);
                    Assert.Equal(golden.Contains("hook.partial.dispose")?1:0,mixed.HookPartialDisposals);Assert.Equal(golden.Contains("partial.dispose")?1:0,probe.PartialDisposals);
                    Assert.Equal(mixed.HookPartialDisposals+probe.PartialDisposals,mixed.CreatedPartials.Count);Assert.Equal(mixed.CreatedPartials.Count,mixed.ReleasedPartials.Count);
                    Assert.All(mixed.CreatedPartials,p=>Assert.Single(mixed.ReleasedPartials,r=>ReferenceEquals(p,r)));
                    Assert.Equal(0,mixed.HookFactoryDisposals);Assert.Equal(0,mixed.HookDependencyDisposals);Assert.Equal(0,probe.FactoryDisposals);Assert.Equal(0,probe.DependencyDisposals);
                    Assert.Equal(scenario=="pre-cancel"||scenario.StartsWith("cancel-in-",StringComparison.Ordinal),token.IsCancellationRequested);
                    operation=new{hookOwned,handlerOwned=owned,isVoid,scenario,golden,trace=core.Phases.ToArray(),actual.Result,errorType=actual.Error?.GetType().FullName,
                        escapingIdentityPreserved=expectedEscaping==null||ReferenceEquals(expectedEscaping,actual.Error),observerIdentityPreserved=ReferenceEquals(expectedObserved,core.ObservedError),exitErrorIdentityPreserved=ReferenceEquals(core.ObserverError??expectedObserved,core.Exit?.Error),
                        createdHooks=hooks,createdHandlers=handlers,operationHookReleases=mixed.ReleasedHooks.Count,operationHandlerReleases=probe.Released.Count,mixed.HookPartialDisposals,probe.PartialDisposals,createdPartialResources=mixed.CreatedPartials.Count,releasedPartialResources=mixed.ReleasedPartials.Count,
                        calls=core.Calls.Select(c=>new{c.Phase,c.Ambient,c.Culture,requestPreserved=ReferenceEquals(c.Phase=="before"?request:saved,c.Request),tokenPreserved=c.Token==token}).ToArray(),
                        handlerCalls=probe.Calls.Select(c=>new{c.Phase,c.Ambient,c.Culture,requestPreserved=ReferenceEquals(saved,c.Request),tokenPreserved=c.Token==token}).ToArray(),
                        hookCalls=mixed.HookCalls.Select(c=>new{c.Phase,c.Ambient,c.Culture,requestPreserved=ReferenceEquals(request,c.Request),tokenPreserved=c.Token==token}).ToArray(),
                        partialCalls=mixed.PartialCalls.Select(c=>new{c.Phase,c.Ambient,c.Culture,requestPreserved=ReferenceEquals(c.Phase=="hook.partial.dispose"?request:saved,c.Request),tokenPreserved=c.Token==token}).ToArray()};
                    if(fixture==null)WriteNestedHookEvidence("mixed-session-reference",hookOwned+"-"+owned+"-"+isVoid+"-"+scenario,operation);
                }
                Assert.Equal(mixed.HookDependencies,mixed.HookDependencyDisposals);Assert.Equal(probe.Dependencies,probe.DependencyDisposals);
                Assert.Equal(golden.Contains("hook-factory.ctor")&&mixed.HookFactoryConstructorError==null?1:0,mixed.HookFactoryDisposals);
                Assert.Equal(golden.Contains("factory.ctor")&&probe.FactoryConstructorError==null?1:0,probe.FactoryDisposals);
            }
            foreach(var session in probe.Borrowed)session.Dispose();foreach(var hook in mixed.BorrowedHooks)hook.Dispose();
            Assert.Equal(probe.Created.Count,probe.Released.Count);Assert.Equal(mixed.CreatedHooks.Count,mixed.ReleasedHooks.Count);
            Assert.All(probe.Created,h=>Assert.Single(probe.Released,r=>ReferenceEquals(h,r)));Assert.All(mixed.CreatedHooks,h=>Assert.Single(mixed.ReleasedHooks,r=>ReferenceEquals(h,r)));
            return JsonSerializer.Serialize(new{operation,finalHookReleases=mixed.ReleasedHooks.Count,finalHandlerReleases=probe.Released.Count,mixed.HookFactoryDisposals,mixed.HookDependencyDisposals,probe.FactoryDisposals,probe.DependencyDisposals});
        }
        finally{core.Cancellation.Dispose();ReferenceSendProbe.Ambient.Value=ambient;CultureInfo.CurrentCulture=culture;}
    }
    public static IEnumerable<object[]> MixedSessionLifetimeCases()
    {foreach(var hookOwned in new[]{false,true})foreach(var handlerOwned in new[]{false,true})foreach(var isVoid in new[]{false,true})foreach(var lifetime in new[]{ServiceLifetime.Scoped,ServiceLifetime.Transient,ServiceLifetime.Singleton})yield return[hookOwned,handlerOwned,isVoid,lifetime];}
    [Theory]
    [MemberData(nameof(MixedSessionLifetimeCases))]
    public void Canonical_mixed_sessions_keep_operation_and_DI_owners_distinct(bool hookOwned,bool handlerOwned,bool isVoid,ServiceLifetime lifetime)
        =>_ = RunMixedSessionLifetimes(hookOwned,handlerOwned,isVoid,lifetime);
    private static string RunMixedSessionLifetimes(bool hookOwned,bool handlerOwned,bool isVoid,ServiceLifetime lifetime,SyncFixture? fixture=null,bool generated=false)
    {
        var mixed=new MixedSessionProbe();var probe=mixed.Handler;var core=mixed.Core;var ambient=ReferenceSendProbe.Ambient.Value;var culture=CultureInfo.CurrentCulture;
        try
        {
            using(var provider=MixedSessionServices(mixed,hookOwned,handlerOwned,isVoid,lifetime,fixture).BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true}))
            {
                for(var s=0;s<2;s++)
                {
                    using(var scope=provider.CreateScope())for(var i=0;i<2;i++)
                    {
                        var request=Activator.CreateInstance(MixedSessionTypes(hookOwned,handlerOwned,isVoid,fixture).Request)!;
                        var actual=fixture!=null&&generated?InvokeSync(fixture,scope.ServiceProvider.GetRequiredService(fixture.Interface),request,default):InvokeMixedSessionReference(scope.ServiceProvider,hookOwned,handlerOwned,isVoid,request,default,fixture);
                        Assert.Null(actual.Error);Assert.Equal(isVoid?null:(object)42,actual.Result);
                        Assert.Equal(hookOwned?s*2+i+1:0,mixed.ReleasedHooks.Count);Assert.Equal(handlerOwned?s*2+i+1:0,probe.Released.Count);
                        var disposed=lifetime==ServiceLifetime.Singleton?0:s*(lifetime==ServiceLifetime.Scoped?1:2);
                        Assert.Equal(disposed,mixed.HookFactoryDisposals);Assert.Equal(disposed,probe.FactoryDisposals);
                    }
                    var count=lifetime==ServiceLifetime.Singleton?0:(s+1)*(lifetime==ServiceLifetime.Scoped?1:2);
                    Assert.Equal(count,mixed.HookFactoryDisposals);Assert.Equal(count,mixed.HookDependencyDisposals);Assert.Equal(count,probe.FactoryDisposals);Assert.Equal(count,probe.DependencyDisposals);
                }
                Assert.Equal(4,mixed.CreatedHooks.Count);Assert.Equal(4,probe.Created.Count);
            }
            var owners=lifetime==ServiceLifetime.Singleton?1:lifetime==ServiceLifetime.Scoped?2:4;
            Assert.Equal(owners,mixed.HookFactoryDisposals);Assert.Equal(owners,mixed.HookDependencyDisposals);Assert.Equal(owners,probe.FactoryDisposals);Assert.Equal(owners,probe.DependencyDisposals);
            foreach(var session in probe.Borrowed)session.Dispose();foreach(var hook in mixed.BorrowedHooks)hook.Dispose();
            Assert.Equal(4,probe.Released.Count);Assert.Equal(4,mixed.ReleasedHooks.Count);
            Assert.Equal(4,probe.Created.Distinct(ReferenceEqualityComparer.Instance).Count());Assert.Equal(4,mixed.CreatedHooks.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.All(probe.Created,h=>Assert.Single(probe.Released,r=>ReferenceEquals(h,r)));Assert.All(mixed.CreatedHooks,h=>Assert.Single(mixed.ReleasedHooks,r=>ReferenceEquals(h,r)));
            var result=new{hookOwned,handlerOwned,isVoid,lifetime=lifetime.ToString(),owners,mixed.HookFactoryDisposals,mixed.HookDependencyDisposals,probe.FactoryDisposals,probe.DependencyDisposals,createdHooks=4,createdHandlers=4,operationHookReleases=hookOwned?4:0,operationHandlerReleases=handlerOwned?4:0,finalHookReleases=mixed.ReleasedHooks.Count,finalHandlerReleases=probe.Released.Count};
            if(fixture==null)WriteNestedHookEvidence("mixed-session-reference-lifetimes",hookOwned+"-"+handlerOwned+"-"+isVoid+"-"+lifetime,result);
            return JsonSerializer.Serialize(result);
        }
        finally{core.Cancellation.Dispose();ReferenceSendProbe.Ambient.Value=ambient;CultureInfo.CurrentCulture=culture;}
    }
    [Theory]
    [InlineData(false,false,false)] [InlineData(false,false,true)] [InlineData(false,true,false)] [InlineData(false,true,true)]
    [InlineData(true,false,false)] [InlineData(true,false,true)] [InlineData(true,true,false)] [InlineData(true,true,true)]
    public void Mixed_session_generation_is_enabled_for_accepted_ownership_combinations(bool hookOwned,bool handlerOwned,bool isVoid)
    {
        var source=HookSessionBackendSource(hookOwned,isVoid,dispatch:false).Replace("typeof(Handler<string>)","HandlerEntry.Session<Handler<string>,HandlerFactory<string>>()")+
            "namespace App { public sealed class HandlerFactory<T> : "+(handlerOwned?"IOwnedSessionFactory":"IBorrowedSessionFactory")+"<Ping,Handler<T>> where T:class { public Handler<T> Create(Ping request,CancellationToken token)=>throw new NotImplementedException(); } }";
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"MixedBoundary"+Guid.NewGuid()),out var output,out var diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));Assert.Empty(diagnostics);
        Assert.Equal(2,driver.GetRunResult().Results.Single().GeneratedSources.Length);
        WriteNestedHookEvidence("mixed-session-boundaries",hookOwned+"-"+handlerOwned+"-"+isVoid,new{hookOwned,handlerOwned,isVoid,source,diagnostics=diagnostics.Select(d=>new{d.Id,message=d.GetMessage()}),generatedSources=2});
    }
}
