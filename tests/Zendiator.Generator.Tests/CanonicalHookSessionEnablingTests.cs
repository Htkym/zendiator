using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public void Enabled_Hook_session_public_registration_resolves_factory_and_leaves_session_to_its_owner(bool owned,bool isVoid)
    {
        var fixture=HookSessionBackendFixture(owned,isVoid); var probe=new HookSessionReferenceProbe(); var core=probe.Core;
        var ambient=ReferenceSendProbe.Ambient.Value; var culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            IServiceCollection services=new ServiceCollection(); services.AddSingleton(probe); services.AddScoped<HookFactoryDependency>();
            Assert.Same(services,fixture.Register.Invoke(null,[services]));
            var hook=fixture.Assembly.GetType("App.Hook`1")!.MakeGenericType(typeof(string));
            Assert.DoesNotContain(services,d=>d.ServiceType==hook);
            Assert.Single(services,d=>d.ServiceType==fixture.Service);
            using(var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true}))
            {
                using(var scope=provider.CreateScope())
                {
                    var actual=InvokeSync(fixture,scope.ServiceProvider.GetRequiredService(fixture.Interface),Activator.CreateInstance(fixture.Request),default);
                    Assert.Null(actual.Error); Assert.Equal(isVoid?null:(object)42,actual.Result);
                    Assert.Equal(("dependency.ctor factory.ctor factory hook.ctor before handler.ctor handler after finally"+(owned?" session.dispose":"")).Split(' '),core.Phases);
                    Assert.Equal(owned?1:0,core.HookDisposals); Assert.Equal(0,core.HandlerDisposals); Assert.Equal(0,probe.FactoryDisposals); Assert.Equal(0,probe.DependencyDisposals);
                }
                Assert.Equal(1,core.HandlerDisposals); Assert.Equal(1,probe.FactoryDisposals); Assert.Equal(1,probe.DependencyDisposals);
            }
            foreach(var resource in probe.Borrowed) resource.Dispose();
            Assert.Single(probe.Created); Assert.Single(probe.Released); Assert.Same(probe.Created[0],probe.Released[0]);
            WriteNestedHookEvidence("hook-session-api",owned+"-"+isVoid,new{owned,isVoid,core.HookDisposals,core.HandlerDisposals,probe.FactoryDisposals,probe.DependencyDisposals,operationDisposed=owned?1:0,externalOwnerDisposed=owned?0:1});
        }
        finally{core.Cancellation.Dispose();ReferenceSendProbe.Ambient.Value=ambient;System.Globalization.CultureInfo.CurrentCulture=culture;}
    }

    // All synchronization below belongs to the user fixture, not to generated orchestration.
    public sealed class SharedHookSessionProbe
    {
        private readonly ConcurrentDictionary<object,NestedOverlapOperation> _operations=new(ReferenceEqualityComparer.Instance);
        private readonly object _gate=new(); private object? _shared;
        public readonly ConcurrentDictionary<string,int> Constructions=new(),Disposals=new();
        public readonly ConcurrentQueue<string> Trace=new();
        public void Add(NestedOverlapOperation operation){foreach(var request in new[]{operation.Input,operation.Outer,operation.Inner}) Assert.True(_operations.TryAdd(request,operation));}
        public void Construct(string owner)=>Constructions.AddOrUpdate(owner,1,(_,count)=>count+1);
        public void DisposeService(string owner)=>Disposals.AddOrUpdate(owner,1,(_,count)=>count+1);
        private NestedOverlapOperation Record(string phase,object request,CancellationToken token)
        {var operation=_operations[request];operation.Calls.Enqueue((phase,request,token));Trace.Enqueue(operation.Name+"."+phase);return operation;}
        public H Borrow<H>(object request,CancellationToken token,Func<H> create) where H:class
        {Record("factory",request,token);lock(_gate)return (H)(_shared??=create());}
        public void ReleaseOwner()=>((IDisposable)_shared!).Dispose();
        public object Before(string owner,object request,CancellationToken token)=>Record(owner+".before",request,token).Outer;
        public int After(string owner,object request,int response,CancellationToken token){Record(owner+".after",request,token);return response+1;}
        public int Handle(object request,CancellationToken token)
        {
            var operation=Record("handler",request,token);
            if(operation.Gate!=null&&!operation.Gate.SignalAndWait(TimeSpan.FromSeconds(10)))throw new TimeoutException("Shared Hook Sends did not overlap.");
            Interlocked.Exchange(ref operation.Reenter,null)?.Invoke();
            if(operation.BodyError is {} error)throw error;return 41;
        }
        public void Observe(string owner,object request,Exception error,CancellationToken token)
        {var operation=Record(owner+".onexception",request,token);Assert.True(operation.Observed.TryAdd(owner,error));}
        public void Finish(string owner,object request,SendExitInfo exit,CancellationToken token)
        {var operation=Record(owner+".finally",request,token);Assert.True(operation.Exits.TryAdd(owner,exit));if(operation.InnerFinallyError is {} error)throw error;}
    }
    private static readonly ConcurrentDictionary<bool,SyncFixture> SharedHookFixtures=new();
    private static SyncFixture SharedHookFixture(bool isVoid)=>SharedHookFixtures.GetOrAdd(isVoid,key=>
    {
        var source=NestedHookBackendSource(key).Replace("GeneratorTests.NestedSendProbe;","GeneratorTests.SharedHookSessionProbe;")
            .Replace("Stage.Hook<Hook<Outer>>(),Stage.Hook<Hook<Inner>>()","Stage.Hook<Hook<Outer>,Factory<string>>()")+
            "namespace App { public sealed class Factory<T> : IBorrowedSessionFactory<Ping,Hook<Outer>>,IDisposable where T:class { private readonly Probe _probe; public Factory(Probe probe){_probe=probe;probe.Construct(\"factory\");} public Hook<Outer> Create(Ping request,CancellationToken token)=>_probe.Borrow(request,token,()=>new Hook<Outer>(_probe)); public void Dispose()=>_probe.DisposeService(\"factory\"); } }";
        return CompileSyncFixture("borrowed",key,sourceOverride:source);
    });
    private static (object? Result,Exception? Error) InvokeSharedHookReference(SyncFixture fixture,IServiceProvider provider,NestedOverlapOperation operation,bool isVoid)
    {
        var hook=fixture.Assembly.GetType("App.Hook`1")!.MakeGenericType(fixture.Assembly.GetType("App.Outer")!);
        var handler=fixture.Assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string));
        var method=typeof(CanonicalSendSyncReference).GetMethod(isVoid?"SendBorrowedHookVoid":"SendBorrowedHookResponse",BindingFlags.Static|BindingFlags.NonPublic)!
            .MakeGenericMethod(isVoid?[fixture.Request,hook,handler,fixture.Service]:[fixture.Request,typeof(int),hook,handler,fixture.Service]);
        try{return(method.Invoke(null,[provider,operation.Input,operation.Token]),null);}catch(TargetInvocationException error){return(null,error.InnerException);}
    }
    private static object AssertSharedHookOperation(NestedOverlapOperation operation,(object? Result,Exception? Error) actual,bool isVoid)
    {
        var body=operation.BodyError;var escaping=operation.InnerFinallyError??body;
        var golden=body!=null?"factory A.before handler A.onexception A.finally":"factory A.before handler A.after A.finally";
        var calls=operation.Calls.ToArray();Assert.Equal(golden.Split(' '),calls.Select(c=>c.Phase));
        Assert.Same(escaping,actual.Error);Assert.Equal(escaping!=null||isVoid?null:(object)42,actual.Result);
        Assert.All(calls,c=>{Assert.Equal(operation.Token,c.Token);Assert.Same(c.Phase is "factory" or "A.before"?operation.Input:operation.Outer,c.Request);});
        if(body==null)Assert.Empty(operation.Observed);else Assert.Same(body,operation.Observed["A"]);
        var exit=operation.Exits["A"];Assert.Equal(body==null,exit.Success);Assert.Same(body,exit.Error);
        string? ErrorRole(Exception? error)=>error==null?null:ReferenceEquals(error,body)?"body":ReferenceEquals(error,operation.InnerFinallyError)?"finally":"unexpected";
        return new{operation.Name,actual.Result,error=ErrorRole(actual.Error),errorType=actual.Error?.GetType().FullName,exit.Success,exitError=ErrorRole(exit.Error),
            calls=calls.Select(c=>new{c.Phase,request=c.Phase is "factory" or "A.before"?"input":"saved",tokenPreserved=c.Token==operation.Token})};
    }
    private static async Task<string> RunSharedHook(bool isVoid,bool reentrant,bool variant,bool generated)
    {
        var fixture=SharedHookFixture(isVoid);var probe=new SharedHookSessionProbe();using var firstToken=new CancellationTokenSource();using var secondToken=new CancellationTokenSource();using var barrier=new Barrier(2);
        var first=new NestedOverlapOperation("outer",fixture.Request,firstToken.Token);var second=new NestedOverlapOperation("inner",fixture.Request,secondToken.Token);
        probe.Add(first);probe.Add(second);
        if(reentrant){second.InnerFinallyError=new Exception("inner Hook Finally identity");if(variant)first.BodyError=second.InnerFinallyError;}
        else{first.Gate=second.Gate=barrier;if(variant){first.BodyError=new OperationCanceledException(firstToken.Token);second.InnerFinallyError=new Exception("other Finally identity");}else second.BodyError=new Exception("other body identity");}
        IServiceCollection services=new ServiceCollection();services.AddSingleton(probe);services.AddSingleton(fixture.Service,fixture.Service);
        Assert.Same(services,fixture.Register.Invoke(null,[services]));object[] snapshots;
        using(var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true}))
        {
            using(var scope=provider.CreateScope())
            {
                var mediator=scope.ServiceProvider.GetRequiredService(fixture.Interface);
                (object? Result,Exception? Error) Invoke(NestedOverlapOperation operation)=>generated?InvokeSync(fixture,mediator,operation.Input,operation.Token):InvokeSharedHookReference(fixture,scope.ServiceProvider,operation,isVoid);
                (object? Result,Exception? Error)[] actual;
                if(reentrant)
                {
                    (object? Result,Exception? Error) inner=default;
                    first.Reenter=()=>{inner=Invoke(second);Assert.Same(second.InnerFinallyError,inner.Error);if(variant)throw inner.Error!;};
                    actual=[Invoke(first),inner];
                    Assert.Equal(("outer.factory outer.A.before outer.handler inner.factory inner.A.before inner.handler inner.A.after inner.A.finally "+
                        (variant?"outer.A.onexception outer.A.finally":"outer.A.after outer.A.finally")).Split(' '),probe.Trace);
                }
                else actual=await Task.WhenAll(new[]{first,second}.Select(op=>Task.Factory.StartNew(()=>Invoke(op),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default))).WaitAsync(TimeSpan.FromSeconds(30));
                snapshots=new[]{AssertSharedHookOperation(first,actual[0],isVoid),AssertSharedHookOperation(second,actual[1],isVoid)};
                Assert.Empty(probe.Disposals);Assert.Equal(1,probe.Constructions["A"]);Assert.Equal(1,probe.Constructions["handler"]);Assert.Equal(1,probe.Constructions["factory"]);
            }
            Assert.Equal(1,probe.Disposals["handler"]);Assert.False(probe.Disposals.ContainsKey("A"));Assert.False(probe.Disposals.ContainsKey("factory"));
        }
        Assert.Equal(1,probe.Disposals["factory"]);Assert.False(probe.Disposals.ContainsKey("A"));probe.ReleaseOwner();Assert.Equal(1,probe.Disposals["A"]);
        return JsonSerializer.Serialize(new{operations=snapshots,trace=reentrant?probe.Trace.ToArray():null,constructions=probe.Constructions.OrderBy(x=>x.Key),disposals=probe.Disposals.OrderBy(x=>x.Key),operationHookDisposals=0,externalOwnerHookDisposals=1});
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Enabled_shared_Borrowed_Hook_parallel_keeps_two_live_scope_operations_separate(bool isVoid)
    {
        foreach(var variant in new[]{false,true})
        {
            var reference=await RunSharedHook(isVoid,false,variant,false);var generated=await RunSharedHook(isVoid,false,variant,true);Assert.Equal(reference,generated);
            WriteNestedHookEvidence("hook-session-shared","parallel-"+isVoid+"-"+variant,new{isVoid,variant,reference=JsonSerializer.Deserialize<JsonElement>(reference),generated=JsonSerializer.Deserialize<JsonElement>(generated)});
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Enabled_shared_Borrowed_Hook_reentry_keeps_handled_or_propagated_inner_Finally_failure_separate(bool isVoid)
    {
        foreach(var variant in new[]{false,true})
        {
            var reference=await RunSharedHook(isVoid,true,variant,false);var generated=await RunSharedHook(isVoid,true,variant,true);Assert.Equal(reference,generated);
            WriteNestedHookEvidence("hook-session-shared","reentry-"+isVoid+"-"+variant,new{isVoid,variant,reference=JsonSerializer.Deserialize<JsonElement>(reference),generated=JsonSerializer.Deserialize<JsonElement>(generated)});
        }
    }
}
