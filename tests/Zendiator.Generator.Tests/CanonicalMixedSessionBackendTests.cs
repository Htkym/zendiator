using Zendiator.SourceGenerator;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly ConcurrentDictionary<(bool HookOwned,bool HandlerOwned,bool Void),SyncFixture> MixedBackendFixtures=new();
    private static string MixedBackendSource(bool hookOwned,bool handlerOwned,bool isVoid,bool dispatch=true)
    {
        var hookContract=hookOwned?"IOwnedSessionFactory":"IBorrowedSessionFactory";var handlerContract=handlerOwned?"IOwnedSessionFactory":"IBorrowedSessionFactory";
        var result=isVoid?"":",int";var afterContract=isVoid?"ISendSuccessObserver<Ping>":"ISendResponseTransform<Ping,int>";
        return $$"""
            using System; using System.Threading; using Zendiator; using Zendiator.DependencyInjection; using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.GeneratorTests.MixedSessionProbe;
            using HookDependency=global::Zendiator.Generator.Tests.GeneratorTests.MixedHookDependency;
            using HandlerDependency=global::Zendiator.Generator.Tests.GeneratorTests.HookHandlerFactoryDependency;
            namespace App {
                public sealed record Ping : ISyncRequest{{(isVoid?"":"<int>")}};
                [ZendiatorHandler] public sealed partial class Handler<T> : ISyncRequestHandler<Ping{{result}}>,IDisposable where T:class {
                    private readonly Probe _probe;private readonly Ping _request;private readonly CancellationToken _token;
                    public Handler(Probe probe,Ping request,CancellationToken token){_probe=probe;_request=request;_token=token;probe.Core.ConstructHandler();}
                    {{(isVoid?"void":"int")}} ISyncRequestHandler<Ping{{result}}>.Handle(Ping request,CancellationToken token){ {{(isVoid?"":"return ")}}_probe.Core.Handle(request,token); }
                    void IDisposable.Dispose()=>_probe.Handler.Release(this,_request,_token);
                }
                public class HookBase<T> : ISendRequestTransform<Ping>,{{afterContract}},ISendExceptionObserver<Ping>,ISendFinallyObserver<Ping>,IDisposable where T:class {
                    private readonly Probe _probe;private readonly Ping _request;private readonly CancellationToken _token;
                    public HookBase(Probe probe,Ping request,CancellationToken token){_probe=probe;_request=request;_token=token;probe.Core.ConstructHook();}
                    Ping ISendRequestTransform<Ping>.Before(Ping request,CancellationToken token)=>(Ping)_probe.Core.Before(request,token)!;
                    {{(isVoid?"void ISendSuccessObserver<Ping>.After(Ping request,CancellationToken token)=>_probe.Core.After(request,0,token);":"int ISendResponseTransform<Ping,int>.After(Ping request,int response,CancellationToken token)=>_probe.Core.After(request,response,token);")}}
                    void ISendExceptionObserver<Ping>.OnException(Ping request,Exception error,CancellationToken token)=>_probe.Core.Observe(request,error,token);
                    void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token)=>_probe.Core.Finish(request,exit,token);
                    void IDisposable.Dispose()=>_probe.ReleaseHook(this,_request,_token);
                }
                public sealed class Hook<T> : HookBase<T> where T:class {public Hook(Probe probe,Ping request,CancellationToken token):base(probe,request,token){ } }
                public class HookFactoryBase<T> : {{hookContract}}<Ping,Hook<T>>,IDisposable where T:class {
                    private readonly Probe _probe;
                    public HookFactoryBase(Probe probe,HookDependency dependency){_probe=probe;probe.Core.Phases.Add("hook-factory.ctor");if(probe.HookFactoryConstructorError is {} error)throw error;}
                    Hook<T> {{hookContract}}<Ping,Hook<T>>.Create(Ping request,CancellationToken token)=>_probe.AcquireHook(request,token,()=>new Hook<T>(_probe,request,token),{{(hookOwned?"false":"true")}});
                    void IDisposable.Dispose(){_probe.Core.Phases.Add("hook-factory.dispose");_probe.HookFactoryDisposals++;}
                }
                public sealed class HookFactory<T> : HookFactoryBase<T> where T:class {public HookFactory(Probe probe,HookDependency dependency):base(probe,dependency){ } }
                public class FactoryBase<T> : {{handlerContract}}<Ping,Handler<T>>,IDisposable where T:class {
                    private readonly Probe _probe;
                    public FactoryBase(Probe probe,HandlerDependency dependency){_probe=probe;probe.Core.Phases.Add("factory.ctor");if(probe.Handler.FactoryConstructorError is {} error)throw error;}
                    Handler<T> {{handlerContract}}<Ping,Handler<T>>.Create(Ping request,CancellationToken token)=>_probe.Handler.Create(request,token,()=>_probe.BuildHandler(request,token,()=>new Handler<T>(_probe,request,token)),{{(handlerOwned?"false":"true")}});
                    void IDisposable.Dispose(){_probe.Core.Phases.Add("factory.dispose");_probe.Handler.FactoryDisposals++;}
                }
                public sealed class Factory<T> : FactoryBase<T> where T:class {public Factory(Probe probe,HandlerDependency dependency):base(probe,dependency){ } }
                [ZendiatorComposition("reads")] public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>{
                        c.Handlers<Ping>(DispatchFamily.SendSync,HandlerEntry.Session<Handler<string>,Factory<string>>());
                        c.Pipeline<Ping>(DispatchFamily.SendSync,Stage.Hook<Hook<string>,HookFactory<string>>());
                    });
                    public static object? ResolveHookFactory(IServiceProvider provider){
                        var probe=provider.GetRequiredService<Probe>();probe.Core.Phases.Add("resolve-hook-factory");if(probe.HookDiError is {} error)throw error;if(probe.NullHookFactory)return null;
                        return new HookFactory<string>(probe,provider.GetRequiredService<HookDependency>());
                    }
                    public static object? ResolveService(IServiceProvider provider){
                        var probe=provider.GetRequiredService<Probe>();probe.Core.Phases.Add("resolve-factory");if(probe.Handler.DiError is {} error)throw error;if(probe.Handler.NullFactory)return null;
                        return new Factory<string>(probe,provider.GetRequiredService<HandlerDependency>());
                    }
                    {{(dispatch?"public static object? Dispatch(object mediator,object? request,CancellationToken token){ "+(isVoid?"":"return ")+"((global::App.Generated.Reads.IZendiator)mediator).Send((Ping)request!,token); "+(isVoid?"return null;":"")+" }":"")}}
                }
            }
            """;
    }
    private static SyncFixture MixedBackendFixture(bool hookOwned,bool handlerOwned,bool isVoid)=>MixedBackendFixtures.GetOrAdd((hookOwned,handlerOwned,isVoid),key=>
        CompileSyncFixture(key.HandlerOwned?"owned":"borrowed",key.Void,sourceOverride:MixedBackendSource(key.HookOwned,key.HandlerOwned,key.Void)));
    [Theory]
    [MemberData(nameof(MixedSessionReferenceCases))]
    public void Candidate_mixed_sessions_match_typed_reference_and_independent_goldens(bool hookOwned,bool owned,bool isVoid,string scenario,string golden)
    {
        var f=MixedBackendFixture(hookOwned,owned,isVoid);var reference=RunMixedSessionScenario(hookOwned,owned,isVoid,scenario,golden,f);var generated=RunMixedSessionScenario(hookOwned,owned,isVoid,scenario,golden,f,generated:true);
        Assert.Equal(reference,generated);
        WriteNestedHookEvidence("mixed-session-differential",hookOwned+"-"+owned+"-"+isVoid+"-"+scenario,new{hookOwned,handlerOwned=owned,isVoid,scenario,golden,reference=JsonSerializer.Deserialize<JsonElement>(reference),generated=JsonSerializer.Deserialize<JsonElement>(generated)});
    }
    [Theory]
    [MemberData(nameof(MixedSessionLifetimeCases))]
    public void Candidate_mixed_sessions_match_reference_DI_and_resource_owners(bool hookOwned,bool handlerOwned,bool isVoid,ServiceLifetime lifetime)
    {
        var f=MixedBackendFixture(hookOwned,handlerOwned,isVoid);var reference=RunMixedSessionLifetimes(hookOwned,handlerOwned,isVoid,lifetime,f);var generated=RunMixedSessionLifetimes(hookOwned,handlerOwned,isVoid,lifetime,f,generated:true);
        Assert.Equal(reference,generated);
        WriteNestedHookEvidence("mixed-session-differential-lifetimes",hookOwned+"-"+handlerOwned+"-"+isVoid+"-"+lifetime,new{hookOwned,handlerOwned,isVoid,lifetime=lifetime.ToString(),reference=JsonSerializer.Deserialize<JsonElement>(reference),generated=JsonSerializer.Deserialize<JsonElement>(generated)});
    }
    public static IEnumerable<object[]> MixedBackendBoundaries()
    {foreach(var hookOwned in new[]{false,true})foreach(var handlerOwned in new[]{false,true})foreach(var isVoid in new[]{false,true})foreach(var shape in new[]{"two-hooks","missing-finally","external-hook-factory","external-handler-factory","wrapper","async"})yield return[hookOwned,handlerOwned,isVoid,shape];}
    [Theory]
    [MemberData(nameof(MixedBackendBoundaries))]
    public void Mixed_session_candidate_blocks_unproved_shapes_before_emission(bool hookOwned,bool handlerOwned,bool isVoid,string shape)
    {
        var source=MixedBackendSource(hookOwned,handlerOwned,isVoid,dispatch:false);
        if(shape=="two-hooks")source=source.Replace("Stage.Hook<Hook<string>,HookFactory<string>>()","Stage.Hook<Hook<string>,HookFactory<string>>(),Stage.Hook<SecondHook>()")+
            "namespace App { public sealed class SecondHook : HookBase<string> {public SecondHook(Probe probe,Ping request,CancellationToken token):base(probe,request,token){}} }";
        if(shape=="missing-finally")source=source.Replace("ISendFinallyObserver<Ping>,","").Replace("void ISendFinallyObserver<Ping>.Finally(Ping request,SendExitInfo exit,CancellationToken token)=>_probe.Core.Finish(request,exit,token);","");
        if(shape=="external-hook-factory")source=source.Replace("Stage.Hook<Hook<string>,HookFactory<string>>()","Stage.Hook<Hook<string>,global::Zendiator.Generator.Tests.GeneratorTests."+(hookOwned?"OwnedMixedHookFactory":"BorrowedMixedHookFactory")+"<Ping,Hook<string>>>()");
        if(shape=="external-handler-factory")source=source.Replace("HandlerEntry.Session<Handler<string>,Factory<string>>()","HandlerEntry.Session<Handler<string>,global::Zendiator.Generator.Tests.GeneratorTests."+(handlerOwned?"OwnedHookHandlerFactory":"BorrowedHookHandlerFactory")+"<Ping,Handler<string>>>()");
        if(shape=="wrapper")source=source.Replace("Stage.Hook<Hook<string>,HookFactory<string>>()","Stage.Wrapper<Wrapper>()")+
            (isVoid?"namespace App {public sealed class Wrapper : ISendWrapper<Ping> {public void Invoke<TNext>(Ping request,TNext next,CancellationToken token) where TNext:ISendContinuation<Ping> =>next.Invoke(request,token);}}":
                "namespace App {public sealed class Wrapper : ISendWrapper<Ping,int> {public int Invoke<TNext>(Ping request,TNext next,CancellationToken token) where TNext:ISendContinuation<Ping,int> =>next.Invoke(request,token);}}");
        if(shape=="async")
        {
            source=source.Replace("using System;","using System; using System.Threading.Tasks;").Replace("ISyncRequest","IRequest").Replace("DispatchFamily.SendSync","DispatchFamily.SendAsync")
                .Replace((isVoid?"void":"int")+" IRequestHandler<Ping"+(isVoid?"":",int")+">.Handle(Ping request,CancellationToken token){ "+(isVoid?"":"return ")+"_probe.Core.Handle(request,token); }",
                    (isVoid?"ValueTask":"ValueTask<int>")+" IRequestHandler<Ping"+(isVoid?"":",int")+">.HandleAsync(Ping request,CancellationToken token){ "+(isVoid?"_probe.Core.Handle(request,token);return default;":"return new(_probe.Core.Handle(request,token));")+" }");
            if(hookOwned)source=source.Replace("ISendFinallyObserver<Ping>,IDisposable where T:class","ISendFinallyObserver<Ping>,IDisposable,IAsyncDisposable where T:class")
                .Replace("void IDisposable.Dispose()=>_probe.ReleaseHook(this,_request,_token);","void IDisposable.Dispose()=>_probe.ReleaseHook(this,_request,_token); public ValueTask DisposeAsync(){((IDisposable)this).Dispose();return default;}");
            if(handlerOwned)source=source.Replace("IRequestHandler<Ping"+(isVoid?"":",int")+">,IDisposable where T:class","IRequestHandler<Ping"+(isVoid?"":",int")+">,IDisposable,IAsyncDisposable where T:class")
                .Replace("void IDisposable.Dispose()=>_probe.Handler.Release(this,_request,_token);","void IDisposable.Dispose()=>_probe.Handler.Release(this,_request,_token); public ValueTask DisposeAsync(){((IDisposable)this).Dispose();return default;}");
        }
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"MixedUnproved"+Guid.NewGuid()),out var output,out var diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));AssertAcceptedSendEmission(driver,diagnostics,shape=="async"?BoundDispatchFamily.SendAsync:BoundDispatchFamily.SendSync,isVoid);
        AssertCanonicalOutputRoles(driver.GetRunResult().Results.Single().GeneratedSources);
        WriteNestedHookEvidence("mixed-session-backend-boundaries",hookOwned+"-"+handlerOwned+"-"+isVoid+"-"+shape,new{hookOwned,handlerOwned,isVoid,shape,source,diagnostics=diagnostics.Select(d=>new{d.Id,message=d.GetMessage()}),generatedExecution=true});
    }
    [Fact]
    public void Mixed_session_body_edits_cache_emission_and_each_ownership_change_updates_its_cleanup()
    {
        var source=MixedBackendSource(true,true,false,dispatch:false);var driver=SyncDriver().RunGenerators(SyncCompilation(source,"MixedIncremental"));
        var first=CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        Assert.Contains("var detached = hook;",first);Assert.Contains("var detached = handler;",first);
        driver=driver.RunGenerators(SyncCompilation(source.Replace("return _probe.Core.Handle(request,token);","return _probe.Core.Handle(request,token)+0;"),"MixedIncremental"));
        Assert.Equal(first,CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalRouteOutputFile"].SelectMany(s=>s.Outputs),o=>Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        driver=driver.RunGenerators(SyncCompilation(MixedBackendSource(false,true,false,dispatch:false),"MixedIncremental"));
        var borrowedHook=CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        Assert.DoesNotContain("var detached = hook;",borrowedHook);Assert.Contains("var detached = handler;",borrowedHook);
        driver=driver.RunGenerators(SyncCompilation(MixedBackendSource(true,false,false,dispatch:false),"MixedIncremental"));
        var borrowedHandler=CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        Assert.Contains("var detached = hook;",borrowedHandler);Assert.DoesNotContain("var detached = handler;",borrowedHandler);
        WriteNestedHookEvidence("mixed-session-backend-incremental","ownership",new{bodyCached=true,hookOwnershipChangesOuterCleanup=true,handlerOwnershipChangesInnerCleanup=true});
    }
}
