using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Sources;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public interface IAsyncOnceGate
{
    int Consumptions { get; }
    void Complete();
    void Fail(Exception error);
}

public sealed class AsyncOnceGate<T>(T value) : IValueTaskSource<T>, IValueTaskSource, IAsyncOnceGate
{
    private ManualResetValueTaskSourceCore<T> _core = new() { RunContinuationsAsynchronously = true };
    public int Consumptions { get; private set; }
    public ValueTask<T> Task => new(this, _core.Version);
    public ValueTask Void => new(this, _core.Version);
    public void Complete() => _core.SetResult(value);
    public void Fail(Exception error) => _core.SetException(error);
    public T GetResult(short token)
    {
        if (++Consumptions != 1) throw new InvalidOperationException("ValueTask source consumed twice.");
        return _core.GetResult(token);
    }
    void IValueTaskSource.GetResult(short token) => GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        Assert.False(flags.HasFlag(ValueTaskSourceOnCompletedFlags.UseSchedulingContext));
        _core.OnCompleted(continuation, state, token, flags);
    }
}

// User callbacks only. The reference and generator use the same closed user types.
public sealed class CanonicalAsyncProbe
{
    public static readonly AsyncLocal<string?> Ambient = new();
    public readonly List<string> Trace = [];
    public readonly List<string?> AmbientSeen = [];
    public readonly List<object> Created = [], Released = [];
    public readonly List<(object Request, CancellationToken Token)> Calls = [];
    public readonly CancellationTokenSource Cancellation = new();
    public readonly List<IAsyncOnceGate> Gates = [];
    public string? PendingPhase;
    public Exception? ConstructorError, FactoryError, HandlerError, CleanupError;
    public bool NullSession, CancelFactory, CancelHandler, FaultedOce, NativeHandler;
    public int FactoryDisposals;
    public void Construct(object receiver) { Trace.Add("construct"); if(ConstructorError!=null)throw ConstructorError; Created.Add(receiver); }
    public void ConstructFactory() { Trace.Add("factory.construct"); if(ConstructorError!=null)throw ConstructorError; }
    private void Hit(string phase, object request, CancellationToken token)
    {
        Trace.Add(phase); AmbientSeen.Add(Ambient.Value); Ambient.Value = phase;
        Calls.Add((request, token));
    }
    public H Create<H>(object receiver, object request, CancellationToken token, Func<H> create) where H : class
    {
        Hit("factory", request, token);
        if (CancelFactory) Cancellation.Cancel();
        if (FactoryError != null) throw FactoryError;
        return NullSession ? null! : create();
    }
    public ValueTask<H> CreateAsync<H>(object receiver, object request, CancellationToken token, Func<H> create) where H : class
    {
        var handler = Create(receiver, request, token, create);
        if (PendingPhase != "factory") return new(handler);
        var gate = new AsyncOnceGate<H>(handler); Gates.Add(gate); return gate.Task;
    }
    public ValueTask<int> Handle(object receiver, object request, CancellationToken token)
    {
        if (NativeHandler) return Native(receiver, request, token);
        Hit("handler", request, token);
        if (CancelHandler) Cancellation.Cancel();
        if (HandlerError != null)
        {
            if (FaultedOce) return new(Task.FromException<int>(HandlerError));
            throw HandlerError;
        }
        if (PendingPhase != "handler") return new(73);
        var gate = new AsyncOnceGate<int>(73); Gates.Add(gate); return gate.Task;
    }
    private async ValueTask<int> Native(object receiver, object request, CancellationToken token)
    {
        Hit("handler", request, token);
        await Task.CompletedTask.ConfigureAwait(false);
        return 73;
    }
    public ValueTask HandleVoid(object receiver, object request, CancellationToken token)
    {
        if (NativeHandler) return NativeVoid(receiver, request, token);
        Hit("handler", request, token);
        if (CancelHandler) Cancellation.Cancel();
        if (HandlerError != null)
        {
            if (FaultedOce) return new(Task.FromException(HandlerError));
            throw HandlerError;
        }
        if (PendingPhase != "handler") return default;
        var gate = new AsyncOnceGate<int>(0); Gates.Add(gate); return gate.Void;
    }
    private async ValueTask NativeVoid(object receiver, object request, CancellationToken token)
    { Hit("handler", request, token); await Task.CompletedTask.ConfigureAwait(false); }
    public ValueTask Cleanup(object receiver)
    {
        Trace.Add("cleanup"); AmbientSeen.Add(Ambient.Value); Ambient.Value = "cleanup";
        Assert.DoesNotContain(Released, value => ReferenceEquals(value, receiver)); Released.Add(receiver);
        if (CleanupError != null) throw CleanupError;
        if (PendingPhase != "cleanup") return default;
        var gate = new AsyncOnceGate<int>(0); Gates.Add(gate); return gate.Void;
    }
    public void DisposeFactory() { Trace.Add("factory.dispose"); FactoryDisposals++; }
}

public sealed partial class GeneratorTests
{
    private sealed record AsyncFixture(Assembly Assembly, Type Mediator, Type Handler, Type Service, Type Request, MethodInfo Register, MethodInfo Dispatch);
    private static readonly ConcurrentDictionary<(string Receiver, bool Void), Lazy<AsyncFixture>> AsyncFixtures = new();
    private static string AsyncSource(string receiver, bool isVoid)
    {
        var asyncFactory = receiver.StartsWith("async-", StringComparison.Ordinal);
        var owned = receiver.EndsWith("owned", StringComparison.Ordinal);
        var role = (asyncFactory ? "IAsync" : "I") + (owned ? "Owned" : "Borrowed") + "SessionFactory";
        var contract = "IRequestHandler<Ping" + (isVoid ? "" : ",int") + ">";
        var entry = receiver == "direct" ? "typeof(Handler<string>)" : "HandlerEntry.Session<Handler<string>,Factory<string>>()";
        var factory = receiver == "direct" ? "" : $$"""
            public class FactoryBase<T> : {{role}}<Ping,Handler<T>>, IDisposable where T:class {
                private readonly Probe _probe;
                public FactoryBase(Probe probe) { _probe=probe; probe.ConstructFactory(); }
                {{(asyncFactory ? "ValueTask<Handler<T>>" : "Handler<T>")}} {{role}}<Ping,Handler<T>>.{{(asyncFactory ? "CreateAsync" : "Create")}}(Ping request,CancellationToken token)
                    =>_probe.{{(asyncFactory ? "CreateAsync" : "Create")}}(this,request,token,()=>new Handler<T>(_probe));
                void IDisposable.Dispose()=>_probe.DisposeFactory();
            }
            public sealed class Factory<T> : FactoryBase<T> where T:class { public Factory(Probe probe):base(probe){} }
            """;
        return $$"""
            using System; using System.Threading; using System.Threading.Tasks; using Zendiator; using Zendiator.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.CanonicalAsyncProbe;
            namespace App {
                public sealed record Ping : IRequest{{(isVoid ? "" : "<int>")}};
                [ZendiatorHandler] public sealed partial class Handler<T> : {{contract}}, IAsyncDisposable where T:class {
                    private readonly Probe _probe;
                    public Handler(Probe probe) { _probe=probe; probe.Construct(this); }
                    {{(isVoid ? "ValueTask" : "ValueTask<int>")}} {{contract}}.HandleAsync(Ping request,CancellationToken token)
                        =>_probe.{{(isVoid ? "HandleVoid" : "Handle")}}(this,request,token);
                    ValueTask IAsyncDisposable.DisposeAsync()=>_probe.Cleanup(this);
                }
                {{factory}}
                [ZendiatorComposition("reads")] public sealed class Reads;
                public static class Bridge {
                    public static IServiceCollection Register(IServiceCollection services)=>services.AddZendiator<Reads>(c=>c.Handlers<Ping>(DispatchFamily.SendAsync,{{entry}}));
                    public static object Dispatch(object mediator,object? request,CancellationToken token)
                        =>((global::App.Generated.Reads.IZendiator)mediator).SendAsync((Ping)request!,token);
                }
            }
            """;
    }
    private static AsyncFixture AsyncRuntimeFixture(string receiver, bool isVoid) => AsyncFixtures.GetOrAdd((receiver,isVoid), key => new(() =>
    {
        var source = AsyncSource(key.Receiver,key.Void);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"CanonicalAsync_"+Guid.NewGuid().ToString("N")),out var output,out var diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));
        var sources = driver.GetRunResult().Results.Single().GeneratedSources; AssertCanonicalOutputRoles(sources);
        AssertCanonicalOutputRoles(sources);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            var folder = Path.Combine(evidence,"async-"+key.Receiver+"-"+key.Void); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"input.cs"),source);
            foreach(var item in sources) File.WriteAllText(Path.Combine(folder,item.HintName),item.SourceText.ToString());
            File.WriteAllBytes(Path.Combine(folder,"generated.dll"),bytes.ToArray());
            File.WriteAllText(Path.Combine(folder,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,errors=0,warnings=0,generatorErrors=diagnostics.Length,generatedSources=sources.Select(s=>s.HintName)}));
        }
        bytes.Position=0; var assembly=AssemblyLoadContext.Default.LoadFromStream(bytes); var bridge=assembly.GetType("App.Bridge")!;
        return new(assembly,assembly.GetType("App.Generated.Reads.Zendiator")!,assembly.GetType("App.Handler`1")!.MakeGenericType(typeof(string)),
            assembly.GetType(receiver=="direct"?"App.Handler`1":"App.Factory`1")!.MakeGenericType(typeof(string)),assembly.GetType("App.Ping")!,bridge.GetMethod("Register")!,bridge.GetMethod("Dispatch")!);
    })).Value;
    private static object AsyncInvoke(AsyncFixture f,string receiver,bool isVoid,bool generated,IServiceProvider provider,object? request,CancellationToken token)
    {
        try
        {
            if(generated) return f.Dispatch.Invoke(null,[provider.GetRequiredService(f.Mediator),request,token])!;
            var name="Send"+(receiver switch{"direct"=>"Direct","owned"=>"Owned","borrowed"=>"Borrowed","async-owned"=>"AsyncOwned",_=>"AsyncBorrowed"})+(isVoid?"Void":"Response");
            var types = new List<Type>{f.Request}; if(!isVoid)types.Add(typeof(int)); types.Add(f.Handler);if(receiver!="direct")types.Add(f.Service);
            return typeof(CanonicalSendAsyncReference).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)!.MakeGenericMethod(types.ToArray()).Invoke(null,[provider,request,token])!;
        }
        catch(TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw; }
    }
    private static Task AsyncTask(object value,bool isVoid)=>isVoid?((ValueTask)value).AsTask():((ValueTask<int>)value).AsTask();
    public static IEnumerable<object[]> AsyncShapes() => from receiver in new[]{"direct","owned","borrowed","async-owned","async-borrowed"} from isVoid in new[]{false,true} select new object[]{receiver,isVoid};
    public static IEnumerable<object[]> AsyncPendingShapes()
    {
        foreach(var row in AsyncShapes())
        {
            yield return [row[0],row[1],"handler"];
            if(((string)row[0]).StartsWith("async-",StringComparison.Ordinal))yield return [row[0],row[1],"factory"];
            if(((string)row[0]).EndsWith("owned",StringComparison.Ordinal))yield return [row[0],row[1],"cleanup"];
        }
    }
    [Theory] [MemberData(nameof(AsyncShapes))]
    public async Task Canonical_SendAsync_standard_API_and_reference_preserve_entry_context_and_DI_ownership(string receiver,bool isVoid)
    {
        var f=AsyncRuntimeFixture(receiver,isVoid);
        foreach(var generated in new[]{false,true}) foreach(var native in new[]{false,true})
        {
            var p=new CanonicalAsyncProbe{NativeHandler=native}; var ambient=CanonicalAsyncProbe.Ambient.Value;
            try
            {
                IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);
                Assert.Contains(services,d=>d.ServiceType==f.Service&&d.Lifetime==ServiceLifetime.Scoped);
                if(receiver!="direct")Assert.DoesNotContain(services,d=>d.ServiceType==f.Handler);
                await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
                await using(var scope=provider.CreateAsyncScope())
                {
                    var request=Activator.CreateInstance(f.Request)!;CanonicalAsyncProbe.Ambient.Value="caller";
                    Assert.Throws<ArgumentNullException>(()=>AsyncInvoke(f,receiver,isVoid,generated,scope.ServiceProvider,null,default));Assert.Empty(p.Trace);
                    p.Cancellation.Cancel();var canceled=AsyncTask(AsyncInvoke(f,receiver,isVoid,generated,scope.ServiceProvider,request,p.Cancellation.Token),isVoid);
                    Assert.True(canceled.IsCanceled);var cancellation=await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>canceled);Assert.Equal(p.Cancellation.Token,cancellation.CancellationToken);Assert.Empty(p.Trace);
                    var task=AsyncTask(AsyncInvoke(f,receiver,isVoid,generated,scope.ServiceProvider,request,default),isVoid);
                    Assert.Equal("caller",CanonicalAsyncProbe.Ambient.Value);await task;if(!isVoid)Assert.Equal(73,await ((Task<int>)task));
                    Assert.Equal(receiver=="direct"?new[]{"caller"}:new[]{"caller","factory"},p.AmbientSeen.Take(receiver=="direct"?1:2));
                    if(receiver.EndsWith("owned",StringComparison.Ordinal))Assert.Equal(native?"factory":"handler",p.AmbientSeen.Last());
                    Assert.All(p.Calls,c=>{Assert.Same(request,c.Request);Assert.Equal(default,c.Token);});
                    Assert.Equal(receiver.EndsWith("owned",StringComparison.Ordinal)?1:0,p.Released.Count);Assert.Equal(0,p.FactoryDisposals);
                    if(receiver is "borrowed" or "async-borrowed")await ((IAsyncDisposable)p.Created.Single()).DisposeAsync();
                }
                Assert.Single(p.Released);Assert.Equal(receiver=="direct"?0:1,p.FactoryDisposals);
                WriteNestedHookEvidence("async-api",receiver+"-"+isVoid+"-"+generated+"-"+native,new{receiver,isVoid,generated,native,p.Trace,p.AmbientSeen,created=p.Created.Count,released=p.Released.Count,p.FactoryDisposals});
            }
            finally{p.Cancellation.Dispose();CanonicalAsyncProbe.Ambient.Value=ambient;}
        }
    }
    [Theory] [MemberData(nameof(AsyncPendingShapes))]
    public async Task Canonical_SendAsync_really_awaits_and_consumes_each_factory_handler_cleanup_once(string receiver,bool isVoid,string phase)
    {
        var f=AsyncRuntimeFixture(receiver,isVoid);
        foreach(var generated in new[]{false,true})
        {
            var p=new CanonicalAsyncProbe{PendingPhase=phase};IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);
            await using var provider=services.BuildServiceProvider();await using(var scope=provider.CreateAsyncScope())
            {
                var task=AsyncTask(AsyncInvoke(f,receiver,isVoid,generated,scope.ServiceProvider,Activator.CreateInstance(f.Request),default),isVoid);
                Assert.False(task.IsCompleted);var gate=Assert.Single(p.Gates);Assert.Equal(0,gate.Consumptions);
                if(phase=="factory")Assert.DoesNotContain("handler",p.Trace);
                gate.Complete();await task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal(1,gate.Consumptions);
                if(!isVoid)Assert.Equal(73,await ((Task<int>)task));
                Assert.Equal(receiver.EndsWith("owned",StringComparison.Ordinal)?1:0,p.Released.Count);
                WriteNestedHookEvidence("async-pending",receiver+"-"+isVoid+"-"+phase+"-"+generated,new{receiver,isVoid,phase,generated,p.Trace,consumptions=gate.Consumptions});
                p.PendingPhase=null;if(receiver is "borrowed" or "async-borrowed")await ((IAsyncDisposable)p.Created.Single()).DisposeAsync();
            }
            p.Cancellation.Dispose();
        }
    }
    public static IEnumerable<object[]> AsyncFailureCases()
    {
        foreach(var row in AsyncShapes())yield return [row[0],row[1],"handler-error"];
        foreach(var receiver in new[]{"direct","async-owned"})foreach(var isVoid in new[]{false,true})foreach(var scenario in new[]{"constructor-error","constructor-oce"})yield return [receiver,isVoid,scenario];
        foreach(var isVoid in new[]{false,true})foreach(var scenario in new[]{"handler-oce","faulted-oce","cleanup-error-over-oce","cleanup-oce-over-error","factory-error","factory-oce","factory-null","cancel-factory","cancel-handler"})
            yield return [scenario is "handler-oce" or "faulted-oce" ? "direct" : "async-owned",isVoid,scenario];
    }
    [Theory] [MemberData(nameof(AsyncFailureCases))]
    public async Task Canonical_SendAsync_returns_native_status_preserves_identity_and_unconditionally_cleans_owned_sessions(string receiver,bool isVoid,string scenario)
    {
        var f=AsyncRuntimeFixture(receiver,isVoid);
        foreach(var generated in new[]{false,true})
        {
            using var other=new CancellationTokenSource();var body=new Exception("body");var cleanup=new Exception("cleanup");var factory=new Exception("factory");
            var oce=new OperationCanceledException("identity",other.Token);var p=new CanonicalAsyncProbe{
                ConstructorError=scenario=="constructor-error"?body:scenario=="constructor-oce"?oce:null,
                HandlerError=scenario is "handler-oce" or "faulted-oce" or "cleanup-error-over-oce"?oce:scenario is "handler-error" or "cleanup-oce-over-error"?body:null,
                CleanupError=scenario=="cleanup-error-over-oce"?cleanup:scenario=="cleanup-oce-over-error"?oce:null,
                FactoryError=scenario=="factory-error"?factory:scenario=="factory-oce"?oce:null,NullSession=scenario=="factory-null",FaultedOce=scenario=="faulted-oce",CancelFactory=scenario=="cancel-factory",CancelHandler=scenario=="cancel-handler"};
            IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);await using var provider=services.BuildServiceProvider();
            await using(var scope=provider.CreateAsyncScope())
            {
                var request=Activator.CreateInstance(f.Request);var task=AsyncTask(AsyncInvoke(f,receiver,isVoid,generated,scope.ServiceProvider,request,p.Cancellation.Token),isVoid);
                Exception? actual=null;try{await task;}catch(Exception e){actual=e;}
                var expected=p.CleanupError??p.ConstructorError??p.FactoryError??p.HandlerError;
                if(p.NullSession)Assert.IsType<InvalidOperationException>(actual);else Assert.Same(expected,actual);
                Assert.Equal(expected is OperationCanceledException,task.IsCanceled);Assert.Equal(expected!=null&&expected is not OperationCanceledException||p.NullSession,task.IsFaulted);
                if(actual is OperationCanceledException escaped)Assert.Equal(other.Token,escaped.CancellationToken);
                var acquired=p.Created.Count!=0;Assert.Equal(receiver.EndsWith("owned",StringComparison.Ordinal)&&acquired?1:0,p.Released.Count);
                if(p.CancelFactory||p.CancelHandler){Assert.True(p.Cancellation.IsCancellationRequested);Assert.Contains("handler",p.Trace);Assert.Contains("cleanup",p.Trace);}
                WriteNestedHookEvidence("async-errors",receiver+"-"+isVoid+"-"+scenario+"-"+generated,new{receiver,isVoid,scenario,generated,p.Trace,task.IsCanceled,task.IsFaulted,identityPreserved=ReferenceEquals(expected,actual),created=p.Created.Count,released=p.Released.Count});
                p.HandlerError=null;p.CleanupError=null;if(receiver is "borrowed" or "async-borrowed")await ((IAsyncDisposable)p.Created.Single()).DisposeAsync();
            }
            p.Cancellation.Dispose();
        }
    }
    [Theory]
    [InlineData("direct",false,"handler",false)] [InlineData("direct",true,"handler",true)]
    [InlineData("owned",false,"handler",true)] [InlineData("owned",true,"handler",false)]
    [InlineData("async-owned",false,"cleanup",true)] [InlineData("async-owned",true,"cleanup",false)]
    [InlineData("owned",false,"cleanup",false)] [InlineData("owned",true,"cleanup",true)]
    public async Task Canonical_SendAsync_pending_failure_preserves_status_identity_and_cleanup_priority(string receiver,bool isVoid,string phase,bool canceled)
    {
        var f=AsyncRuntimeFixture(receiver,isVoid);
        foreach(var generated in new[]{false,true})
        {
            using var token=new CancellationTokenSource();Exception error=canceled?new OperationCanceledException("pending",token.Token):new Exception("pending");
            var p=new CanonicalAsyncProbe{PendingPhase=phase,HandlerError=phase=="cleanup"?(canceled?new Exception("body"):new OperationCanceledException("body",token.Token)):null};
            IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);await using var provider=services.BuildServiceProvider();
            await using(var scope=provider.CreateAsyncScope())
            {
                var task=AsyncTask(AsyncInvoke(f,receiver,isVoid,generated,scope.ServiceProvider,Activator.CreateInstance(f.Request),default),isVoid);
                Assert.False(task.IsCompleted);var gate=Assert.Single(p.Gates);Assert.Equal(0,gate.Consumptions);gate.Fail(error);
                var escaped=await Record.ExceptionAsync(()=>task.WaitAsync(TimeSpan.FromSeconds(10)));Assert.Same(error,escaped);Assert.Equal(canceled,task.IsCanceled);Assert.Equal(!canceled,task.IsFaulted);Assert.Equal(1,gate.Consumptions);
                Assert.Equal(receiver=="direct"?0:1,p.Released.Count);
                WriteNestedHookEvidence("async-pending-failure",receiver+"-"+isVoid+"-"+phase+"-"+generated,new{receiver,isVoid,phase,generated,canceled,identityPreserved=ReferenceEquals(error,escaped),consumptions=gate.Consumptions,p.Trace});
                p.PendingPhase=null;p.HandlerError=null;
            }
            p.Cancellation.Dispose();
        }
    }
    [Theory] [InlineData("all")] [InlineData("struct")] [InlineData("implicit")]
    public void Canonical_SendAsync_unsupported_routes_keep_diagnostics_and_emit_no_execution(string shape)
        =>AssertUnsupportedAsyncShape(shape);

    [Fact]
    public void Canonical_SendAsync_valid_opaque_wrapper_has_public_generated_entry()
    {
        var source=AsyncSource("direct",false);var start=source.IndexOf("public static object Dispatch",StringComparison.Ordinal);source=source.Remove(start,source.IndexOf(';',start)-start+1);
        source=source.Replace("c=>c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler<string>))","c=>{c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler<string>));c.Pipeline<Ping>(DispatchFamily.SendAsync,Stage.Wrapper<Wrap>());}")+"namespace App {public sealed class Wrap:IAsyncSendWrapper<Ping,int>{public ValueTask<int> InvokeAsync<TNext>(Ping r,TNext next,CancellationToken t) where TNext:IAsyncSendContinuation<Ping,int> =>next.InvokeAsync(r,t);}}";
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"ExistingOpaqueWrapper"),out var output,out var diagnostics);
        Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));AssertCanonicalOutputRoles(driver.GetRunResult().Results.Single().GeneratedSources);
    }

    private static void AssertUnsupportedAsyncShape(string shape)
    {
        var source=AsyncSource("direct",false).Replace("public static object Dispatch(object mediator,object? request,CancellationToken token)\n        =>((global::App.Generated.Reads.IZendiator)mediator).SendAsync((Ping)request!,token);","");
        // Remove the generated-API consumer while testing absence of that API.
        var start=source.IndexOf("public static object Dispatch",StringComparison.Ordinal);if(start>=0){var end=source.IndexOf(';',start);source=source.Remove(start,end-start+1);}
        source=shape switch{
            "struct"=>source.Replace("sealed record Ping","readonly record struct Ping"),
            "all"=>source.Replace("DispatchFamily.SendAsync","DispatchFamily.SendAllAsync"),
            "implicit"=>source.Replace("c=>c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler<string>))","c=>{c.AddHandler<Handler<string>>();c.Pipeline<Ping>(DispatchFamily.SendAsync);}"),
            "hook"=>source.Replace("c=>c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler<string>))","c=>{c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler<string>));c.Pipeline<Ping>(DispatchFamily.SendAsync,Stage.Hook<Hook>());}")+"namespace App {public sealed class Hook:ISendFinallyObserver<Ping>{public void Finally(Ping r,SendExitInfo e,CancellationToken t){}}}",
            _=>source.Replace("c=>c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler<string>))","c=>{c.Handlers<Ping>(DispatchFamily.SendAsync,typeof(Handler<string>));c.Pipeline<Ping>(DispatchFamily.SendAsync,Stage.Wrapper<Wrap>());}")+"namespace App {public sealed class Wrap:IAsyncSendWrapper<Ping,int>{public ValueTask<int> InvokeAsync<TNext>(Ping r,TNext next,CancellationToken t) where TNext:IAsyncSendContinuation<Ping,int> =>next.InvokeAsync(r,t);}}"};
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"AsyncUnsupported"+shape),out var output,out var diagnostics);
        if(shape=="struct"){
            Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));AssertCanonicalOutputRoles(driver.GetRunResult().Results.Single().GeneratedSources);return;
        }
        Assert.Equal("ZEN0037",Assert.Single(diagnostics).Id);AssertNoCanonicalOutput(driver.GetRunResult().Results.Single().GeneratedSources);
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
    }

    [Fact]
    public void Canonical_SendAsync_body_edits_cache_emission_and_coexists_with_SendSync()
    {
        var source=AsyncSource("direct",false);
        var input=SyncCompilation(source,"AsyncIncremental");var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(input,out _,out _);
        var text=CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources);
        var changed=source.Replace("=>_probe.Handle(this,request,token);","{ return _probe.Handle(this,request,token); }");
        driver=driver.RunGeneratorsAndUpdateCompilation(input.ReplaceSyntaxTree(input.SyntaxTrees.Single(),Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(changed,SyncParseOptions)),out _,out var diagnostics);
        Assert.Empty(diagnostics);Assert.Equal(text,CanonicalRouteSource(driver.GetRunResult().Results.Single().GeneratedSources));
        Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalRouteOutputFile"].SelectMany(s=>s.Outputs),o=>Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
        var other=SyncRuntimeSource("direct",false).Replace("namespace App {","namespace SyncApp {").Replace("global::App.Generated.Reads","global::SyncApp.Generated.Reads").Replace("ZendiatorComposition(\"reads\")","ZendiatorComposition(\"sync-reads\")");
        var both=SyncCompilation(source,"BothFamilies").AddSyntaxTrees(Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(other,SyncParseOptions));
        driver=SyncDriver().RunGeneratorsAndUpdateCompilation(both,out var output,out diagnostics);
        Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error));
        Assert.Equal(4,driver.GetRunResult().Results.Single().GeneratedSources.Length);
        WriteNestedHookEvidence("async-incremental","body-and-coexistence",new{bodyEditCached=true,coexistingSources=4});
    }

    [Theory] [InlineData("direct",false)] [InlineData("direct",true)] [InlineData("owned",false)] [InlineData("owned",true)] [InlineData("borrowed",false)] [InlineData("borrowed",true)]
    public void Canonical_SendAsync_shared_registrar_preserves_existing_SendSync_generation(string receiver,bool isVoid)
    {
        CompileSyncFixture(receiver,isVoid);
    }
}
