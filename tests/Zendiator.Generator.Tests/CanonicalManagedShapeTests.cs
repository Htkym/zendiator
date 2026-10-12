using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed class ManagedShapeProbe
{
    public readonly List<string> Trace=[];public readonly List<(string Phase,int Value)> Requests=[];
    public readonly HashSet<object> Created=new(ReferenceEqualityComparer.Instance),Released=new(ReferenceEqualityComparer.Instance);
    public readonly List<IAsyncOnceGate> Gates=[];public object? HandlerResponse,AfterInput,AfterOutput;
    public readonly CancellationTokenSource Token=new();public bool Pending,CancelBefore;
    public void Hit(string phase,int value){Trace.Add(phase);Requests.Add((phase,value));if(phase=="before"&&CancelBefore)Token.Cancel();}
    public void Construct(object h)=>Assert.True(Created.Add(h));public void Dispose(object h){Trace.Add("dispose");Assert.True(Released.Add(h));}
    public ValueTask<T> Body<T>(int value,T result){Hit("body",value);HandlerResponse=result;if(!Pending)return new(result);var gate=new AsyncOnceGate<T>(result);Gates.Add(gate);return gate.Task;}
}
public sealed partial class GeneratorTests
{
    private sealed record ManagedShapeFixture(Assembly Assembly,Type Mediator,Type Request,MethodInfo Register,MethodInfo Dispatch,MethodInfo Reference,Type Result,string Body);
    private static readonly ConcurrentDictionary<(bool Async,bool Struct,string Result,bool Wrapper),Lazy<ManagedShapeFixture>> ManagedShapeFixtures=new();
    private static string ManagedShapeSource(bool async,bool valueRequest,string result,bool wrapper)
    {
        var r=result=="array"?"int[]":result=="nullable"?"int?":"Reply";var ret=async?"ValueTask<"+r+">":r;var prefix=async?"IAsyncSend":"ISend";
        var make=result=="array"?"new[]{request.Value}":result=="nullable"?"(int?)request.Value":"new Reply(request.Value,2,3,4,5,6,7,8)";
        var transform=result=="array"?"new[]{value[0]+1}":result=="nullable"?"value+1":"value with{A=value.A+1}";
        var source=$$"""
            using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
            using Probe=global::Zendiator.Generator.Tests.ManagedShapeProbe;
            namespace App{
                public {{(valueRequest?"readonly record struct":"sealed record")}} Ping(int Value):{{(async?"IRequest":"ISyncRequest")}}<{{r}}>;
                public readonly record struct Reply(long A,long B,long C,long D,long E,long F,long G,long H);
                public sealed class Hook:ISendRequestTransform<Ping>,{{prefix}}ResponseTransform<Ping,{{r}}>,ISendFinallyObserver<Ping>{
                    private readonly Probe _p;public Hook(Probe p){_p=p;}
                    public Ping Before(Ping request,CancellationToken t){_p.Hit("before",request.Value);return request with{Value=request.Value+1};}
                    public {{ret}} {{(async?"AfterAsync":"After")}}(Ping request,{{r}} value,CancellationToken t){_p.Hit("after",request.Value);_p.AfterInput=value;{{r}} next={{transform}};_p.AfterOutput=next;return {{(async?"new(next)":"next")}};}
                    public void Finally(Ping request,SendExitInfo e,CancellationToken t){_p.Hit("finally",request.Value);}
                }
                [ZendiatorHandler]public sealed class Handler:{{(async?"IRequestHandler":"ISyncRequestHandler")}}<Ping,{{r}}>,{{(async?"IAsyncDisposable":"IDisposable")}}{
                    private readonly Probe _p;public Handler(Probe p){_p=p;p.Construct(this);}
                    public {{ret}} {{(async?"HandleAsync":"Handle")}}(Ping request,CancellationToken t){{{(async?"return _p.Body(request.Value,"+make+");":"_p.Hit(\"body\",request.Value);"+r+" value="+make+";_p.HandlerResponse=value;return value;")}}}
                    public {{(async?"ValueTask DisposeAsync()":"void Dispose()")}}{_p.Dispose(this);{{(async?"return default;":"")}}}
                }
                public sealed class Factory:IOwnedSessionFactory<Ping,Handler>{private readonly Probe _p;public Factory(Probe p){_p=p;}public Handler Create(Ping request,CancellationToken t){_p.Hit("factory",request.Value);return new(_p);} }
                {{(wrapper?"public sealed class W:"+prefix+"Wrapper<Ping,"+r+">{public "+ret+" "+(async?"InvokeAsync":"Invoke")+"<TNext>(Ping request,TNext next,CancellationToken t)where TNext:"+(async?"IAsyncSendContinuation":"ISendContinuation")+"<Ping,"+r+"> =>next."+(async?"InvokeAsync":"Invoke")+"(request with{Value=request.Value+10},t);}":"")}}
                [ZendiatorComposition("shapes")]public sealed class Reads;
                public static class Bridge{
                    public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{c.Handlers<Ping>(DispatchFamily.{{(async?"SendAsync":"SendSync")}},HandlerEntry.Session<Handler,Factory>());c.Pipeline<Ping>(DispatchFamily.{{(async?"SendAsync":"SendSync")}},{{(wrapper?"Stage.Wrapper<W>(),":"")}}Stage.Hook<Hook>());});
                    public static object Dispatch(object m,object request,CancellationToken t)=>((global::App.Generated.Reads.IZendiator)m).{{(async?"SendAsync":"Send")}}((Ping)request,t);
                }
            }
            """;
        var nullGuard=valueRequest?"":"ArgumentNullException.ThrowIfNull(request);";
        var region="private static "+(async?"async ":"")+ret+" Region(IServiceProvider p,Ping request,CancellationToken t){t.ThrowIfCancellationRequested();var hook=p.GetRequiredService<Hook>();var saved=request;bool success=false;Exception? error=null;try{saved=hook.Before(saved,t);"+(valueRequest?"":"ArgumentNullException.ThrowIfNull(saved,\"request\");")+"t.ThrowIfCancellationRequested();Handler? h=p.GetRequiredService<Factory>().Create(saved,t)??throw new InvalidOperationException();"+r+" value;try{value="+(async?"await ":"")+"h."+(async?"HandleAsync":"Handle")+"(saved,t)"+(async?".ConfigureAwait(false)":"")+";}finally{var detached=h;h=null;"+(async?"await detached.DisposeAsync().ConfigureAwait(false);":"detached.Dispose();")+"}value="+(async?"await ":"")+"hook."+(async?"AfterAsync":"After")+"(saved,value,t)"+(async?".ConfigureAwait(false)":"")+";success=true;return value;}catch(Exception failure){error=failure;throw;}finally{hook.Finally(saved,new(success,error),t);}}";
        var oracle="namespace App{public static class Oracle{public static "+ret+" Send(IServiceProvider p,Ping request,CancellationToken t){"+nullGuard+"return "+(wrapper?"Outer(p,request,t)":"Region(p,request,t)")+";}"+region;
        if(wrapper)oracle+="private static "+(async?"async ":"")+ret+" Outer(IServiceProvider p,Ping request,CancellationToken t){t.ThrowIfCancellationRequested();return "+(async?"await ":"")+"p.GetRequiredService<W>()."+(async?"InvokeAsync":"Invoke")+"(request,new Next(p),t)"+(async?".ConfigureAwait(false)":"")+";}private readonly struct Next:"+(async?"IAsyncSendContinuation":"ISendContinuation")+"<Ping,"+r+">{private readonly IServiceProvider _p;public Next(IServiceProvider p){_p=p;}public "+ret+" "+(async?"InvokeAsync":"Invoke")+"(Ping request,CancellationToken t){"+nullGuard+"return Region(_p,request,t);}}";
        return source+oracle+"}}";
    }
    private static ManagedShapeFixture ManagedShapeRuntimeFixture(bool async,bool valueRequest,string result,bool wrapper)=>ManagedShapeFixtures.GetOrAdd((async,valueRequest,result,wrapper),k=>new(()=>{
        var source=ManagedShapeSource(k.Async,k.Struct,k.Result,k.Wrapper);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"Shape_"+Guid.NewGuid().ToString("N")),out var output,out var diagnostics);Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var sources=driver.GetRunResult().Results.Single().GeneratedSources;Assert.Equal(2,sources.Count(s=>s.HintName!="Zendiator.GeneratedCatalog.g.cs"));
        var body=sources.Single(s=>!s.HintName.Contains("Interceptor",StringComparison.Ordinal)&&s.HintName!="Zendiator.GeneratedCatalog.g.cs").SourceText.ToString();if(k.Struct)Assert.DoesNotContain("ArgumentNullException.ThrowIfNull(request)",body);Assert.DoesNotContain("GetAwaiter",body);if(!k.Async)Assert.DoesNotContain("ValueTask",body);
        var e=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");if(!string.IsNullOrEmpty(e)){var p=Path.Combine(e,"managed-shape-"+k.Async+"-"+k.Struct+"-"+k.Result+"-"+k.Wrapper);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);foreach(var s in sources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=diagnostics.Length}));}
        bytes.Position=0;var a=AssemblyLoadContext.Default.LoadFromStream(bytes);var b=a.GetType("App.Bridge")!;var response=k.Result=="array"?typeof(int[]):k.Result=="nullable"?typeof(int?):a.GetType("App.Reply")!;return new(a,a.GetType("App.Generated.Reads.Zendiator")!,a.GetType("App.Ping")!,b.GetMethod("Register")!,b.GetMethod("Dispatch")!,a.GetType("App.Oracle")!.GetMethod("Send")!,response,body);
    })).Value;
    [Theory][InlineData(false,true,"large",false)][InlineData(false,false,"array",false)][InlineData(true,true,"array",false)][InlineData(true,true,"nullable",false)][InlineData(false,true,"array",true)][InlineData(true,true,"large",true)]
    public async Task Managed_shapes_struct_copy_array_identity_nullable_and_large_response_match_reference_API(bool async,bool valueRequest,string result,bool wrapper)
    {var f=ManagedShapeRuntimeFixture(async,valueRequest,result,wrapper);foreach(var generated in new[]{false,true})await RunManagedShape(f,async,valueRequest,result,wrapper,generated,false,false);}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Managed_shapes_real_suspend_or_before_cancellation_preserve_struct_value_and_cleanup(bool cancel)
    {var f=ManagedShapeRuntimeFixture(true,true,"array",false);foreach(var generated in new[]{false,true})await RunManagedShape(f,true,true,"array",false,generated,!cancel,cancel);}
    private static async Task RunManagedShape(ManagedShapeFixture f,bool async,bool valueRequest,string result,bool wrapper,bool generated,bool pending,bool cancel)
    {
        var p=new ManagedShapeProbe{Pending=pending,CancelBefore=cancel};try{IServiceCollection services=new ServiceCollection();services.AddSingleton(p);f.Register.Invoke(null,[services]);await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();var request=Activator.CreateInstance(f.Request,[5])!;object? value=null;Exception? error=null;
            object Invoke(){try{return generated?f.Dispatch.Invoke(null,[scope.ServiceProvider.GetRequiredService(f.Mediator),request,p.Token.Token])!:f.Reference.Invoke(null,[scope.ServiceProvider,request,p.Token.Token])!;}catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException!).Throw();throw;}}
            if(async){var boxed=Invoke();var task=(Task)boxed.GetType().GetMethod("AsTask")!.Invoke(boxed,null)!;if(pending){Assert.False(task.IsCompleted);Assert.Single(p.Gates).Complete();}try{await task.WaitAsync(TimeSpan.FromSeconds(10));value=task.GetType().GetProperty("Result")!.GetValue(task);}catch(Exception e){error=e;}Assert.Equal(cancel,task.IsCanceled);}else{try{value=Invoke();}catch(Exception e){error=e;}}
            Assert.Equal(5,f.Request.GetProperty("Value")!.GetValue(request));if(cancel){var oce=Assert.IsAssignableFrom<OperationCanceledException>(error);Assert.Equal(p.Token.Token,oce.CancellationToken);Assert.Empty(p.Created);Assert.Equal(new[]{"before","finally"},p.Trace);}else{Assert.Null(error);var expected=wrapper?17:7;Assert.Equal(expected,result=="array"?((int[])value!)[0]:result=="nullable"?(int)value!:Convert.ToInt32(f.Result.GetProperty("A")!.GetValue(value)));Assert.Equal(new[]{"before","factory","body","dispose","after","finally"},p.Trace);Assert.Equal(p.Created.Count,p.Released.Count);Assert.Equal(p.HandlerResponse,p.AfterInput);if(result=="array"){Assert.Same(p.HandlerResponse,p.AfterInput);Assert.NotSame(p.AfterInput,p.AfterOutput);Assert.Same(p.AfterOutput,value);}}
            Assert.All(p.Gates,g=>Assert.Equal(1,g.Consumptions));WriteNestedHookEvidence("managed-shape-observations",async+"-"+valueRequest+"-"+result+"-"+wrapper+"-"+generated+"-"+pending+"-"+cancel,new{async,valueRequest,result,wrapper,generated,pending,cancel,p.Trace,p.Requests,error=error?.GetType().Name,created=p.Created.Count,released=p.Released.Count,consumptions=p.Gates.Select(g=>g.Consumptions)});
        }finally{p.Token.Dispose();}
    }
    [Fact]
    public void Managed_shapes_keep_accepted_reference_request_and_result_generated_bodies_unchanged()
    {StandaloneFixture(true,false,"owned",false);WrapperHookRuntimeFixture(true,false,false,"owned");AsyncRuntimeFixture("direct",false);}
}
