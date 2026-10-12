using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    public sealed class HookHandlerSessionProbe
    {
        public readonly ReferenceSendProbe Core=new();
        public Exception? DiError,FactoryConstructorError,FactoryError,PartialError,CleanupError;
        public bool NullFactory,NullSession,CancelFactory,CancelCleanup;
        public int Dependencies,DependencyDisposals,FactoryDisposals,PartialDisposals;
        public readonly List<object> Created=new(),Released=new();
        public readonly List<IDisposable> Borrowed=new();
        public readonly List<(string Phase,object Request,CancellationToken Token,string? Ambient,string Culture)> Calls=new();
        public H Create<H>(object request,CancellationToken token,Func<H> create,bool borrowed) where H:class,IDisposable
        {
            Record("factory",request,token);if(CancelFactory)Core.Cancellation.Cancel();if(FactoryError is {} error)throw error;
            if(PartialError is {} partial){Core.Phases.Add("partial.acquire");try{throw partial;}finally{Core.Phases.Add("partial.dispose");PartialDisposals++;}}
            if(NullSession)return null!;var handler=create();Created.Add(handler);if(borrowed)Borrowed.Add(handler);return handler;
        }
        public void Release(object handler,object request,CancellationToken token)
        {Record("handler.dispose",request,token);Released.Add(handler);Core.HandlerDisposals++;if(CancelCleanup)Core.Cancellation.Cancel();if(CleanupError is {} error)throw error;}
        private void Record(string phase,object request,CancellationToken token)
        {Core.Phases.Add(phase);Calls.Add((phase,request,token,ReferenceSendProbe.Ambient.Value,CultureInfo.CurrentCulture.Name));ReferenceSendProbe.Ambient.Value=phase;CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(phase=="factory"?"it-IT":"ko-KR");}
    }
    public sealed class HookHandlerFactoryDependency : IDisposable
    {
        private readonly HookHandlerSessionProbe _probe;
        public HookHandlerFactoryDependency(HookHandlerSessionProbe probe){_probe=probe;probe.Core.Phases.Add("dependency.ctor");probe.Dependencies++;}
        public void Dispose(){_probe.Core.Phases.Add("dependency.dispose");_probe.DependencyDisposals++;}
    }
    public abstract class HookHandlerFactory<R,H> : IDisposable where R:class where H:class,IDisposable
    {
        private readonly HookHandlerSessionProbe _probe;private readonly Func<R,CancellationToken,H> _create;
        protected HookHandlerFactory(HookHandlerSessionProbe probe,HookHandlerFactoryDependency dependency,Func<R,CancellationToken,H> create)
        {_probe=probe;_create=create;probe.Core.Phases.Add("factory.ctor");if(probe.FactoryConstructorError is {} error)throw error;}
        protected H Create(R request,CancellationToken token,bool borrowed)=>_probe.Create(request,token,()=>_create(request,token),borrowed);
        public void Dispose(){_probe.Core.Phases.Add("factory.dispose");_probe.FactoryDisposals++;}
    }
    public sealed class OwnedHookHandlerFactory<R,H>(HookHandlerSessionProbe probe,HookHandlerFactoryDependency dependency,Func<R,CancellationToken,H> create)
        :HookHandlerFactory<R,H>(probe,dependency,create),IOwnedSessionFactory<R,H> where R:class where H:class,IDisposable
    {public H Create(R request,CancellationToken token)=>Create(request,token,false);}
    public sealed class BorrowedHookHandlerFactory<R,H>(HookHandlerSessionProbe probe,HookHandlerFactoryDependency dependency,Func<R,CancellationToken,H> create)
        :HookHandlerFactory<R,H>(probe,dependency,create),IBorrowedSessionFactory<R,H> where R:class where H:class,IDisposable
    {public H Create(R request,CancellationToken token)=>Create(request,token,true);}
    public sealed class ResponseHookHandlerSession : ISyncRequestHandler<ReferenceResponseRequest,int>,IDisposable
    {
        private readonly HookHandlerSessionProbe _probe;private readonly ReferenceResponseRequest _request;private readonly CancellationToken _token;
        public ResponseHookHandlerSession(HookHandlerSessionProbe probe,ReferenceResponseRequest request,CancellationToken token){_probe=probe;_request=request;_token=token;probe.Core.ConstructHandler();}
        public int Handle(ReferenceResponseRequest request,CancellationToken token)=>_probe.Core.Handle(request,token);
        public void Dispose()=>_probe.Release(this,_request,_token);
    }
    public sealed class VoidHookHandlerSession : ISyncRequestHandler<ReferenceVoidRequest>,IDisposable
    {
        private readonly HookHandlerSessionProbe _probe;private readonly ReferenceVoidRequest _request;private readonly CancellationToken _token;
        public VoidHookHandlerSession(HookHandlerSessionProbe probe,ReferenceVoidRequest request,CancellationToken token){_probe=probe;_request=request;_token=token;probe.Core.ConstructHandler();}
        public void Handle(ReferenceVoidRequest request,CancellationToken token)=>_probe.Core.Handle(request,token);
        public void Dispose()=>_probe.Release(this,_request,_token);
    }
    private static IServiceCollection HookHandlerServices(HookHandlerSessionProbe probe,bool owned,bool isVoid,ServiceLifetime lifetime,SyncFixture? fixture=null)
    {
        IServiceCollection services=new ServiceCollection();services.AddSingleton(probe);services.AddSingleton(probe.Core);
        if(fixture is {} f)
        {
            var hook=f.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string));
            var handler=f.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string));
            services.Add(ServiceDescriptor.Describe(hook,hook,lifetime));
            services.Add(ServiceDescriptor.Describe(handler,_=>throw new Exception("A Handler session must come from its factory."),ServiceLifetime.Scoped));
            services.Add(ServiceDescriptor.Describe(typeof(HookHandlerFactoryDependency),typeof(HookHandlerFactoryDependency),lifetime));
            services.Add(ServiceDescriptor.Describe(f.Service,provider=>((Func<IServiceProvider,object?>)f.ResolveService.CreateDelegate(typeof(Func<IServiceProvider,object?>)))(provider)!,lifetime));
            f.Register.Invoke(null,[services]);return services;
        }
        services.Add(ServiceDescriptor.Describe(typeof(HookHandlerFactoryDependency),typeof(HookHandlerFactoryDependency),lifetime));
        services.Add(ServiceDescriptor.Describe(typeof(ReferenceResponseHook),typeof(ReferenceResponseHook),lifetime));
        services.Add(ServiceDescriptor.Describe(typeof(ReferenceVoidHook),typeof(ReferenceVoidHook),lifetime));
        var factory=isVoid?owned?typeof(OwnedHookHandlerFactory<ReferenceVoidRequest,VoidHookHandlerSession>):typeof(BorrowedHookHandlerFactory<ReferenceVoidRequest,VoidHookHandlerSession>):
            owned?typeof(OwnedHookHandlerFactory<ReferenceResponseRequest,ResponseHookHandlerSession>):typeof(BorrowedHookHandlerFactory<ReferenceResponseRequest,ResponseHookHandlerSession>);
        services.Add(ServiceDescriptor.Describe(factory,provider=>{
            probe.Core.Phases.Add("resolve-factory");if(probe.DiError is {} error)throw error;if(probe.NullFactory)return null!;
            var dependency=provider.GetRequiredService<HookHandlerFactoryDependency>();
            return isVoid?owned?new OwnedHookHandlerFactory<ReferenceVoidRequest,VoidHookHandlerSession>(probe,dependency,(r,t)=>new(probe,r,t)):
                new BorrowedHookHandlerFactory<ReferenceVoidRequest,VoidHookHandlerSession>(probe,dependency,(r,t)=>new(probe,r,t)):
                owned?new OwnedHookHandlerFactory<ReferenceResponseRequest,ResponseHookHandlerSession>(probe,dependency,(r,t)=>new(probe,r,t)):
                new BorrowedHookHandlerFactory<ReferenceResponseRequest,ResponseHookHandlerSession>(probe,dependency,(r,t)=>new(probe,r,t));
        },lifetime));
        services.AddScoped<ResponseHookHandlerSession>(_=>throw new Exception("A Handler session must come from its factory."));
        services.AddScoped<VoidHookHandlerSession>(_=>throw new Exception("A Handler session must come from its factory."));return services;
    }
    private static (object? Result,Exception? Error) InvokeHookHandlerReference(IServiceProvider provider,bool owned,bool isVoid,object? request,CancellationToken token)
    {
        try
        {
            if(isVoid)
            {
                if(owned)CanonicalSendSyncReference.SendHookOwnedHandlerVoid<ReferenceVoidRequest,ReferenceVoidHook,VoidHookHandlerSession,OwnedHookHandlerFactory<ReferenceVoidRequest,VoidHookHandlerSession>>(provider,(ReferenceVoidRequest)request!,token);
                else CanonicalSendSyncReference.SendHookBorrowedHandlerVoid<ReferenceVoidRequest,ReferenceVoidHook,VoidHookHandlerSession,BorrowedHookHandlerFactory<ReferenceVoidRequest,VoidHookHandlerSession>>(provider,(ReferenceVoidRequest)request!,token);
                return(null,null);
            }
            return(owned?CanonicalSendSyncReference.SendHookOwnedHandlerResponse<ReferenceResponseRequest,int,ReferenceResponseHook,ResponseHookHandlerSession,OwnedHookHandlerFactory<ReferenceResponseRequest,ResponseHookHandlerSession>>(provider,(ReferenceResponseRequest)request!,token):
                CanonicalSendSyncReference.SendHookBorrowedHandlerResponse<ReferenceResponseRequest,int,ReferenceResponseHook,ResponseHookHandlerSession,BorrowedHookHandlerFactory<ReferenceResponseRequest,ResponseHookHandlerSession>>(provider,(ReferenceResponseRequest)request!,token),null);
        }
        catch(Exception error){return(null,error);}
    }
    public static IEnumerable<object[]> HookHandlerReferenceCases()
    {
        const string prefix="hook.ctor before resolve-factory dependency.ctor factory.ctor factory ";
        foreach(var data in SingleHookReferenceCases())foreach(var owned in new[]{false,true})
        {
            var scenario=(string)data[1];var golden=((string)data[2]).Replace("handler.resolve","resolve-factory").Replace("handler.ctor","resolve-factory dependency.ctor factory.ctor factory handler.ctor");
            if(owned)golden=golden.Replace("handler after","handler handler.dispose after").Replace("handler onexception","handler handler.dispose onexception");
            yield return[owned,data[0],scenario,golden];
        }
        foreach(var owned in new[]{false,true})foreach(var isVoid in new[]{false,true})foreach(var scenario in new[]{"factory-di-null","factory-ctor-error","factory-error","factory-oce","factory-null","partial-error","partial-oce","cancel-in-factory"})
        {
            var golden=scenario switch{"factory-di-null"=>"hook.ctor before resolve-factory onexception finally","factory-ctor-error"=>"hook.ctor before resolve-factory dependency.ctor factory.ctor onexception finally",
                "partial-error" or "partial-oce"=>prefix+"partial.acquire partial.dispose onexception finally","cancel-in-factory"=>prefix+"handler.ctor handler "+(owned?"handler.dispose ":"")+"after finally",_=>prefix+"onexception finally"};
            yield return[owned,isVoid,scenario,golden];
        }
        foreach(var isVoid in new[]{false,true})foreach(var scenario in new[]{"cleanup-error-success","cleanup-oce-success","cleanup-error-over-body","cleanup-oce-over-body","cleanup-error-over-oce","cleanup-oce-over-oce","cancel-in-cleanup","observer-error-over-cleanup","finally-error-over-cleanup","finally-oce-over-cleanup"})
            yield return[true,isVoid,scenario,prefix+"handler.ctor handler handler.dispose "+(scenario=="cancel-in-cleanup"?"after finally":"onexception finally")];
    }
    [Theory]
    [MemberData(nameof(HookHandlerReferenceCases))]
    public void Canonical_DI_Hook_Handler_session_preserves_inner_cleanup_before_outer_observation(bool owned,bool isVoid,string scenario,string phases)
        =>_ = RunHookHandlerScenario(owned,isVoid,scenario,phases);
    private static string RunHookHandlerScenario(bool owned,bool isVoid,string scenario,string phases,SyncFixture? fixture=null,bool generated=false)
    {
        object? operation=null;
        var probe=new HookHandlerSessionProbe();var core=probe.Core;var ambient=ReferenceSendProbe.Ambient.Value;var culture=CultureInfo.CurrentCulture;
        ReferenceSendProbe.Ambient.Value="caller";CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;using var otherToken=new CancellationTokenSource();var token=core.Cancellation.Token;
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
        try
        {
            var services=HookHandlerServices(probe,owned,isVoid,ServiceLifetime.Scoped,fixture);
            if(scenario=="stage-di-error")services.AddScoped(fixture!=null?fixture.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string)):isVoid?typeof(ReferenceVoidHook):typeof(ReferenceResponseHook),_=>{core.Phases.Add("stage.resolve");throw body;});
            using(var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true}))
            {
                using(var scope=provider.CreateScope())
                {
                    var actual=fixture==null?InvokeHookHandlerReference(scope.ServiceProvider,owned,isVoid,request,token):generated?InvokeSync(fixture,scope.ServiceProvider.GetRequiredService(fixture.Interface),request,token):InvokeCompiledHookHandlerReference(fixture,scope.ServiceProvider,owned,isVoid,request,token);var golden=phases.Split(' ',StringSplitOptions.RemoveEmptyEntries);Assert.Equal(golden,core.Phases);
                    var expectedInner=probe.CleanupError??probe.DiError??probe.FactoryConstructorError??probe.FactoryError??probe.PartialError??core.HandlerConstructorError??core.HandlerError;
                    var escaping=core.FinallyError??core.ObserverError??expectedInner??core.StageConstructorError??core.BeforeError??core.AfterError??(scenario=="stage-di-error"?body:null);
                    if(escaping!=null)Assert.Same(escaping,actual.Error);
                    else if(scenario is "null-request" or "before-null")Assert.Equal("request",Assert.IsType<ArgumentNullException>(actual.Error).ParamName);
                    else if(scenario is "pre-cancel" or "cancel-in-before")Assert.Equal(token,Assert.IsType<OperationCanceledException>(actual.Error).CancellationToken);
                    else if(scenario is "factory-null" or "factory-di-null")Assert.IsType<InvalidOperationException>(actual.Error);
                    else{Assert.Null(actual.Error);Assert.Equal(isVoid?null:(object)42,actual.Result);}
                    var observedExpected=scenario is "before-null" or "cancel-in-before" or "factory-null" or "factory-di-null"?actual.Error:golden.Contains("onexception")?expectedInner??core.BeforeError??core.AfterError:null;
                    Assert.Same(observedExpected,core.ObservedError);
                    if(golden.Contains("finally")){var exit=Assert.IsType<SendExitInfo>(core.Exit);Assert.Equal(!golden.Contains("onexception"),exit.Success);Assert.Same(core.ObserverError??observedExpected,exit.Error);}else Assert.Null(core.Exit);
                    var saved=scenario=="before-null"?null:scenario=="replace-request"?replacement:request;
                    Assert.All(core.Calls,c=>{Assert.Equal(token,c.Token);Assert.Same(c.Phase=="before"?request:saved,c.Request);});
                    Assert.All(probe.Calls,c=>{Assert.Equal(token,c.Token);Assert.Same(saved,c.Request);});
                    foreach(var call in core.Calls)
                    {
                        var prior=call.Phase switch{
                            "before"=>"caller", "handler"=>"factory", "after"=>owned?"handler.dispose":"handler",
                            "onexception"=>scenario is "before-error" or "before-null" or "cancel-in-before" or "handler-di-error" or "factory-di-null" or "factory-ctor-error"?"before":
                                core.AfterError!=null?"after":probe.CleanupError!=null||owned&&core.HandlerError!=null?"handler.dispose":core.HandlerError!=null?"handler":"factory",
                            "finally"=>core.ObservedError!=null?"onexception":"after",_=>throw new InvalidOperationException()};
                        Assert.Equal(prior,call.Ambient);Assert.Equal(prior switch{"caller"=>"","factory"=>"it-IT","handler.dispose"=>"ko-KR",_=>ReferenceSendProbe.CultureFor(prior)},call.Culture);
                    }
                    foreach(var call in probe.Calls)
                    {var prior=call.Phase=="factory"?"before":"handler";Assert.Equal(prior,call.Ambient);Assert.Equal(ReferenceSendProbe.CultureFor(prior),call.Culture);}
                    var hasSession=golden.Contains("handler");Assert.Equal(hasSession?1:0,probe.Created.Count);Assert.Equal(owned&&hasSession?1:0,probe.Released.Count);
                    Assert.Equal(0,core.HookDisposals);Assert.Equal(0,probe.FactoryDisposals);Assert.Equal(0,probe.DependencyDisposals);
                    Assert.Equal(scenario.StartsWith("partial-",StringComparison.Ordinal)?1:0,probe.PartialDisposals);
                    Assert.Equal(scenario is "pre-cancel" or "cancel-in-before" or "cancel-in-factory" or "cancel-in-handler" or "cancel-in-after" or "cancel-in-finally" or "cancel-in-cleanup",token.IsCancellationRequested);
                    Assert.Equal(core.Calls.Count==0?"caller":"finally",ReferenceSendProbe.Ambient.Value);
                    operation=new{owned,isVoid,scenario,golden,trace=core.Phases.ToArray(),actual.Result,errorType=actual.Error?.GetType().FullName,
                        escapingIdentityPreserved=escaping==null||ReferenceEquals(escaping,actual.Error),observerIdentityPreserved=ReferenceEquals(observedExpected,core.ObservedError),exitIdentityPreserved=ReferenceEquals(core.ObserverError??observedExpected,core.Exit?.Error),
                        calls=core.Calls.Select(c=>new{c.Phase,c.Ambient,c.Culture,requestPreserved=ReferenceEquals(c.Phase=="before"?request:saved,c.Request),tokenPreserved=c.Token==token}).ToArray(),
                        innerCalls=probe.Calls.Select(c=>new{c.Phase,c.Ambient,c.Culture,requestPreserved=ReferenceEquals(saved,c.Request),tokenPreserved=c.Token==token}).ToArray(),createdSessions=probe.Created.Count,operationReleasedSessions=probe.Released.Count,core.HookDisposals,probe.FactoryDisposals,probe.DependencyDisposals};
                    if(fixture==null)WriteNestedHookEvidence("hook-handler-session-reference",owned+"-"+isVoid+"-"+scenario,operation);
                }
                Assert.Equal(core.HookConstructed?1:0,core.HookDisposals);Assert.Equal(probe.Dependencies,probe.DependencyDisposals);
                Assert.Equal(phases.Contains("factory.ctor",StringComparison.Ordinal)&&scenario!="factory-ctor-error"?1:0,probe.FactoryDisposals);
            }
            Assert.Equal(owned?probe.Created.Count:0,probe.Released.Count);foreach(var session in probe.Borrowed)session.Dispose();Assert.Equal(probe.Created.Count,probe.Released.Count);
            Assert.All(probe.Created,session=>Assert.Single(probe.Released,item=>ReferenceEquals(item,session)));
            return JsonSerializer.Serialize(new{operation,finalOwnerReleasedSessions=probe.Released.Count,core.HookDisposals,probe.FactoryDisposals,probe.DependencyDisposals});
        }
        finally{core.Cancellation.Dispose();ReferenceSendProbe.Ambient.Value=ambient;CultureInfo.CurrentCulture=culture;}
    }
    public static IEnumerable<object[]> HookHandlerReferenceLifetimes()
    {foreach(var owned in new[]{false,true})foreach(var isVoid in new[]{false,true})foreach(var lifetime in new[]{ServiceLifetime.Scoped,ServiceLifetime.Transient,ServiceLifetime.Singleton})yield return[owned,isVoid,lifetime];}
    [Theory]
    [MemberData(nameof(HookHandlerReferenceLifetimes))]
    public void Canonical_DI_Hook_Handler_session_keeps_DI_services_and_operation_resources_distinct(bool owned,bool isVoid,ServiceLifetime lifetime)
        =>_ = RunHookHandlerLifetimes(owned,isVoid,lifetime);
    private static string RunHookHandlerLifetimes(bool owned,bool isVoid,ServiceLifetime lifetime,SyncFixture? fixture=null,bool generated=false)
    {
        var probe=new HookHandlerSessionProbe();var core=probe.Core;var ambient=ReferenceSendProbe.Ambient.Value;var culture=CultureInfo.CurrentCulture;
        try
        {
            using(var provider=HookHandlerServices(probe,owned,isVoid,lifetime,fixture).BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true}))
            {
                for(var s=0;s<2;s++)
                {
                    using(var scope=provider.CreateScope())for(var i=0;i<2;i++)
                    {object request=fixture!=null?Activator.CreateInstance(fixture.Request)!:isVoid?new ReferenceVoidRequest():new ReferenceResponseRequest();var actual=fixture==null?InvokeHookHandlerReference(scope.ServiceProvider,owned,isVoid,request,default):generated?InvokeSync(fixture,scope.ServiceProvider.GetRequiredService(fixture.Interface),request,default):InvokeCompiledHookHandlerReference(fixture,scope.ServiceProvider,owned,isVoid,request,default);Assert.Null(actual.Error);Assert.Equal(isVoid?null:(object)42,actual.Result);Assert.Equal(owned?s*2+i+1:0,probe.Released.Count);Assert.Equal(lifetime==ServiceLifetime.Singleton?0:s*(lifetime==ServiceLifetime.Scoped?1:2),core.HookDisposals);}
                    var count=lifetime==ServiceLifetime.Singleton?0:(s+1)*(lifetime==ServiceLifetime.Scoped?1:2);Assert.Equal(count,core.HookDisposals);Assert.Equal(count,probe.FactoryDisposals);Assert.Equal(count,probe.DependencyDisposals);
                }
                Assert.Equal(4,probe.Created.Count);Assert.Equal(owned?4:0,probe.Released.Count);
            }
            var owners=lifetime==ServiceLifetime.Singleton?1:lifetime==ServiceLifetime.Scoped?2:4;Assert.Equal(owners,core.HookDisposals);Assert.Equal(owners,probe.FactoryDisposals);Assert.Equal(owners,probe.DependencyDisposals);
            foreach(var session in probe.Borrowed)session.Dispose();Assert.Equal(4,probe.Released.Count);Assert.Equal(4,probe.Created.Distinct(ReferenceEqualityComparer.Instance).Count());
            var result=new{owned,isVoid,lifetime=lifetime.ToString(),owners,core.HookDisposals,probe.FactoryDisposals,probe.DependencyDisposals,createdSessions=4,operationReleasedSessions=owned?4:0,finalOwnerReleasedSessions=probe.Released.Count};
            if(fixture==null)WriteNestedHookEvidence("hook-handler-session-reference-lifetimes",owned+"-"+isVoid+"-"+lifetime,result);
            return JsonSerializer.Serialize(result);
        }
        finally{core.Cancellation.Dispose();ReferenceSendProbe.Ambient.Value=ambient;CultureInfo.CurrentCulture=culture;}
    }
}
