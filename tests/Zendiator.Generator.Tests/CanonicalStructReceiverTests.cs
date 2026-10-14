using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed class StructReceiverProbe
{
    public readonly List<string> Trace=[];
    public readonly List<object> Created=[],Released=[];
    public readonly InvalidOperationException Failure=new("struct receiver body");
    public bool Fail;
    public void Create(object value)=>Created.Add(value);
    public void Release(object value){Assert.DoesNotContain(Released,r=>ReferenceEquals(r,value));Released.Add(value);}
}

public sealed partial class GeneratorTests
{
    private sealed record StructReceiverFixture(Assembly Types,Type Mediator,Type Request,MethodInfo Register,MethodInfo Dispatch,string Body);
    private static StructReceiverFixture StructReceiverRuntimeFixture(string profile,bool metadata)
    {
        var async=profile.EndsWith("async",StringComparison.Ordinal);var factory=profile.StartsWith("factory",StringComparison.Ordinal);
        var full=profile.StartsWith("full",StringComparison.Ordinal)||profile is "wrapper-sync" or "factory-sync";
        var wrapper=profile.StartsWith("wrapper",StringComparison.Ordinal)||profile=="factory-async";
        var hooks=profile.StartsWith("direct",StringComparison.Ordinal)?0:profile is "full-async" or "factory-sync" or "factory-async"?1:2;
        string Completion(string value)=>async?"new("+value+")":value;
        string Return(string type)=>async?"ValueTask<"+type+">":type;
        var a=async?"Async":"";var request=async?"IRequest<int>":"ISyncRequest<int>";
        var contracts=async?"IRequestHandler<Ping,int>":"ISyncRequestHandler<Ping,int>";
        var hookContract=(async?"IAsyncSendRequestTransform":"ISendRequestTransform")+"<Ping>";
        if(full)hookContract+=","+(async?"IAsyncSendResponseTransform":"ISendResponseTransform")+"<Ping,int>,"+(async?"IAsyncSendExceptionObserver":"ISendExceptionObserver")+"<Ping>,"+(async?"IAsyncSendFinallyObserver":"ISendFinallyObserver")+"<Ping>";
        var cleanup=async?"public ValueTask DisposeAsync(){_p.Release(this);return default;}":"public void Dispose()=>_p.Release(this);";
        string Hook(string name)
        {
            var declaration="public "+(factory?"sealed class ":"struct ")+name+":"+hookContract+","+(factory&&async?"IAsyncDisposable":"IDisposable")+"{public int State,Disposed;private readonly Probe _p;public "+name+"(Probe p){State=Disposed=0;_p=p;"+(factory?"p.Create(this);":"")+"}";
            declaration+="public "+Return("Ping")+" Before"+a+"(Ping r,CancellationToken t){State++;_p.Trace.Add(\""+name+".before\");return "+Completion("r")+";}";
            if(full)declaration+="public "+Return("int")+" After"+a+"(Ping r,int v,CancellationToken t){State++;_p.Trace.Add(\""+name+".after\");return "+Completion("v")+";}public "+(async?"ValueTask":"void")+" OnException"+a+"(Ping r,Exception e,CancellationToken t){State++;_p.Trace.Add(\""+name+".exception\");"+(async?"return default;":"")+"}public "+(async?"ValueTask":"void")+" Finally"+a+"(Ping r,SendExitInfo e,CancellationToken t){State++;_p.Trace.Add(\""+name+".finally\");"+(async?"return default;":"")+"}";
            return declaration+(factory?cleanup:"public void Dispose(){Disposed++;}")+"}";
        }
        string Wrapper(string name)=>"public "+(factory?"sealed class ":"struct ")+name+":"+(async?"IAsyncSendWrapper":"ISendWrapper")+"<Ping,int>,"+(factory&&async?"IAsyncDisposable":"IDisposable")+"{public int State,Disposed;private readonly Probe _p;public "+name+"(Probe p){State=Disposed=0;_p=p;"+(factory?"p.Create(this);":"")+"}public "+(async?"async ":"")+Return("int")+" Invoke"+a+"<N>(Ping r,N next,CancellationToken t)where N:"+(async?"IAsyncSendContinuation":"ISendContinuation")+"<Ping,int>{State++;_p.Trace.Add(\""+name+".enter\");var result="+(async?"await ":"")+"next.Invoke"+a+"(r,t);State++;_p.Trace.Add(\""+name+".exit\");return result;}"+(factory?cleanup:"public void Dispose(){Disposed++;}")+"}";
        string Factory(string target)=>"public struct "+target+"F:"+(async?"IAsyncOwnedSessionFactory":"IOwnedSessionFactory")+"<Ping,"+target+">,IDisposable{public int State,Disposed;private readonly Probe _p;public "+target+"F(Probe p){State=Disposed=0;_p=p;}public "+Return(target)+" Create"+a+"(Ping r,CancellationToken t){State++;return "+Completion("new "+target+"(_p)")+";}public void Dispose(){Disposed++;}}";
        var types="using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Probe=global::Zendiator.Generator.Tests.StructReceiverProbe;namespace Boxes{public sealed record Ping(int Value):"+request+";[ZendiatorHandler]public "+(factory?"sealed class ":"struct ")+"Handler:"+contracts+","+(factory&&async?"IAsyncDisposable":"IDisposable")+"{public int State,Disposed;private readonly Probe _p;public Handler(Probe p){State=Disposed=0;_p=p;"+(factory?"p.Create(this);":"")+"}public "+Return("int")+" Handle"+a+"(Ping r,CancellationToken t){State++;_p.Trace.Add(\"body\");if(_p.Fail)throw _p.Failure;return "+Completion("r.Value+State")+";}"+(factory?cleanup:"public void Dispose(){Disposed++;}")+"}";
        var stages=new List<string>();
        for(var i=0;i<hooks;i++){var name="K"+i;types+=Hook(name);if(factory)types+=Factory(name);stages.Add("Stage.Hook<"+name+(factory?","+name+"F":"")+">()");}
        if(wrapper){types+=Wrapper("W0");if(factory)types+=Factory("W0");stages.Insert(Math.Min(1,stages.Count),"Stage.Wrapper<W0"+(factory?",W0F":"")+">()");if(!factory&&async){types+=Wrapper("W1");stages.Add("Stage.Wrapper<W1>()");}}
        if(factory)types+=Factory("Handler");types+="}";
        var consumer="using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Boxes;namespace StructConsumer{[ZendiatorComposition(\"boxes\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{c.Handlers<Ping>(DispatchFamily.Send"+(async?"Async":"Sync")+","+(factory?"HandlerEntry.Session<Handler,HandlerF>()":"typeof(Handler)")+ ");"+(stages.Count==0?"":"c.Pipeline<Ping>(DispatchFamily.Send"+(async?"Async":"Sync")+","+string.Join(",",stages)+");")+"});public static async Task<int> Dispatch(object m,object r){"+(async?"return await ((global::StructConsumer.Generated.Reads.IZendiator)m).SendAsync((Ping)r);":"await Task.CompletedTask;return ((global::StructConsumer.Generated.Reads.IZendiator)m).Send((Ping)r);")+"}}}";
        if(profile=="wrapper-async")consumer=consumer[..^1]+"public readonly struct IdentityNext:IAsyncSendContinuation<Ping,int>{public ValueTask<int> InvokeAsync(Ping r,CancellationToken t)=>new(r.Value);}public static class Oracle{public static Task<int> Send(object box,object request)=>((IAsyncSendWrapper<Ping,int>)box).InvokeAsync((Ping)request,new IdentityNext(),default).AsTask();}}";
        var references=new List<MetadataReference>();Assembly? producer=null;
        if(metadata){var producerBytes=CompileExplicitMetadataProducer(types,"struct-"+profile);references.Add(MetadataReference.CreateFromImage(producerBytes));using var producerStream=new MemoryStream(producerBytes);producer=AssemblyLoadContext.Default.LoadFromStream(producerStream);}
        var source=metadata?consumer:consumer[..consumer.IndexOf("namespace StructConsumer",StringComparison.Ordinal)]+"using System;using Probe=global::Zendiator.Generator.Tests.StructReceiverProbe;"+types[types.IndexOf("namespace Boxes",StringComparison.Ordinal)..]+consumer[consumer.IndexOf("namespace StructConsumer",StringComparison.Ordinal)..];
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"StructConsumer_"+Guid.NewGuid().ToString("N")).AddReferences(references),out var output,out var diagnostics);
        Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var sources=driver.GetRunResult().Results.Single().GeneratedSources;
        AssertCanonicalOutputRoles(sources);var body=CanonicalDispatchSource(sources);
        Assert.Contains("GetRequiredService(_provider, typeof(global::Boxes.",body);Assert.DoesNotContain("GetRequiredService<global::Boxes.",body);Assert.DoesNotContain("GetAwaiter",body);
        var evidence=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");if(!string.IsNullOrEmpty(evidence)){var p=Path.Combine(evidence,"generated-artifacts","struct-"+profile);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);foreach(var s in sources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=0,metadata,boxedLookup=true}));}
        bytes.Position=0;var assembly=AssemblyLoadContext.Default.LoadFromStream(bytes);var bridge=assembly.GetType("StructConsumer.Bridge")!;return new(producer??assembly,assembly.GetType("StructConsumer.Generated.Reads.Zendiator")!, (producer??assembly).GetType("Boxes.Ping")!,bridge.GetMethod("Register")!,bridge.GetMethod("Dispatch")!,body);
    }
    [Theory]
    [InlineData("direct-sync",false)] [InlineData("direct-async",true)]
    [InlineData("full-sync",false)] [InlineData("full-async",false)]
    [InlineData("partial-async",false)] [InlineData("wrapper-sync",false)]
    [InlineData("wrapper-async",true)] [InlineData("factory-sync",false)] [InlineData("factory-async",true)]
    public async Task Struct_receivers_preserve_DI_box_state_and_factory_session_ownership(string profile,bool metadata)
    {
        var f=StructReceiverRuntimeFixture(profile,metadata);var p=new StructReceiverProbe();IServiceCollection collection=new MultiCollection();collection.AddSingleton(p);
        var types=f.Types.GetTypes().Where(t=>t.Namespace=="Boxes"&&t.IsValueType).OrderBy(t=>t.Name,StringComparer.Ordinal).ToArray();var boxes=types.ToDictionary(t=>t,t=>Activator.CreateInstance(t,[p])!);
        foreach(var pair in boxes){var box=pair.Value;collection.Add(ServiceDescriptor.Singleton(pair.Key,_=>box));}
        Assert.Same(collection,f.Register.Invoke(null,[collection]));Assert.All(types,t=>Assert.Single(collection,d=>d.ServiceType==t));
        using(var provider=new MultiContainer(collection)){
            var mediator=provider.GetService(f.Mediator)!;
            for(var i=0;i<3;i++){
                p.Fail=i==1;p.Trace.Clear();var request=Activator.CreateInstance(f.Request,[10])!;var call=(Task<int>)f.Dispatch.Invoke(null,[mediator,request])!;
                if(p.Fail)Assert.Same(p.Failure,await Assert.ThrowsAsync<InvalidOperationException>(()=>call));else Assert.Equal(10+(profile.StartsWith("factory",StringComparison.Ordinal)?1:i+1),await call.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Single(p.Trace,phase=>phase=="body");Assert.Equal(p.Created.Count,p.Released.Count);Assert.All(boxes.Values,box=>Assert.Equal(0,box.GetType().GetField("Disposed")!.GetValue(box)));
            }
            foreach(var pair in boxes){
                var expected=pair.Key.Name.EndsWith("F",StringComparison.Ordinal)||pair.Key.Name=="Handler"||profile=="partial-async"||profile=="wrapper-async"&&pair.Key.Name.StartsWith("K",StringComparison.Ordinal)?3:pair.Key.Name.StartsWith("W",StringComparison.Ordinal)?5:9;
                if(profile=="wrapper-async"&&pair.Key.Name.StartsWith("W",StringComparison.Ordinal)){
                    // C# async instance methods copy a struct's this into the state machine.
                    // Compare an ordinary typed invocation; the generator must preserve that behavior.
                    var reference=Activator.CreateInstance(pair.Key,[new StructReceiverProbe()])!;
                    var oracle=f.Mediator.Assembly.GetType("StructConsumer.Oracle")!.GetMethod("Send")!;
                    Assert.Equal(10,await ((Task<int>)oracle.Invoke(null,[reference,Activator.CreateInstance(f.Request,[10])!])!).WaitAsync(TimeSpan.FromSeconds(10)));
                    expected=(int)pair.Key.GetField("State")!.GetValue(reference)!;Assert.Equal(0,expected);((IDisposable)reference).Dispose();
                }
                Assert.Equal(expected,pair.Key.GetField("State")!.GetValue(pair.Value));Assert.Equal(3,provider.Resolutions.Count(t=>t==pair.Key));Assert.Same(pair.Value,provider.GetService(pair.Key));
            }
            Assert.DoesNotContain(typeof(IServiceScopeFactory),provider.Resolutions);
        }
        Assert.All(boxes.Values,box=>Assert.Equal(1,box.GetType().GetField("Disposed")!.GetValue(box)));Assert.All(p.Created,created=>Assert.Contains(p.Released,released=>ReferenceEquals(created,released)));
        WriteNestedHookEvidence("struct-receiver-observations",profile,new{profile,metadata,operations=3,preservedBoxes=boxes.Select(x=>new{type=x.Key.FullName,state=x.Key.GetField("State")!.GetValue(x.Value),disposed=x.Key.GetField("Disposed")!.GetValue(x.Value)}),ownedCreated=p.Created.Count,ownedReleased=p.Released.Count,exceptionIdentityPreserved=true,implicitScopes=0});
    }
    [Fact]
    public void Struct_receivers_preserve_class_backend_generation_without_runtime_replay()
    {Multi_route_single_backend_generation_preserves_execution_body_with_standard_provider_contract();}
}
