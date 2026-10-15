using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static AllGenericCase[] AllGenericWrapperCases => [AllGenericCases[0], AllGenericCases[2], AllGenericCases[5], AllGenericCases[7]];
    private static string AllGenericWrapperDeclaration(AllGenericCase c)
    {
        var async = c.Async; var isVoid = c.Void;
        var ret = async ? isVoid ? "ValueTask" : "ValueTask<int>" : isVoid ? "void" : "int";
        var invoke = async ? "InvokeAsync" : "Invoke"; var prefix = async ? "IAsyncSend" : "ISend";
        var next = (isVoid ? "" : "value=") + (async ? "await " : "") + "next." + invoke + "(r,t)" + (async ? ".ConfigureAwait(false)" : "") + ";";
        return $$"""
            public class W:{{prefix}}Wrapper<Ping{{(isVoid ? "" : ",int")}}>,{{(async ? "IAsyncDisposable" : "IDisposable")}}{
                private readonly Probe p;public W(Probe p){this.p=p;p.Created(this,"W",false,true);}
                public {{(async ? "async " : "")}}{{ret}} {{invoke}}<N>(Ping r,N next,CancellationToken t)where N:{{(async ? "IAsyncSendContinuation" : "ISendContinuation")}}<Ping{{(isVoid ? "" : ",int")}}>{
                    r.Mark(p,"W.before",t);
                    if(p.Skip&&p.Branch==2){r.Mark(p,"W.skip",t);{{(isVoid ? "return;" : "return 70;")}}}
                    {{(isVoid ? "" : "int value;")}}
                    try{ {{next}} }
                    catch(Exception e)when(p.Retry&&ReferenceEquals(e,p.Failure)){
                        r.Mark(p,"W.caught",t,e);{{next.Replace("(r,t)", "(r.Change(r.Value+20),t)", StringComparison.Ordinal)}}
                    }
                    r.Mark(p,"W.after",t);{{(isVoid ? "" : "return value+1;")}}
                }
                {{(async ? "public ValueTask DisposeAsync(){p.Cleanup(this);return default;}" : "public void Dispose()=>p.Cleanup(this);")}}
            }
            """;
    }
    private const string AllGenericWrapperProbeSource = """
        namespace GenericCases{
        using Zendiator.Generator.Tests;
        public sealed class Shared{public int Version;}
        public sealed class Probe{
            public readonly CancellationTokenSource Token=new();public readonly Exception Failure=new FormatException("planned");
            public readonly List<object> Events=new();public readonly List<int> Handlers=new();public readonly List<string> Factories=new(),FactoryEnds=new(),Trace=new();
            public readonly Dictionary<object,(string Id,bool Owned,bool Direct)> Resources=new(ReferenceEqualityComparer.Instance);
            public readonly HashSet<object> Released=new(ReferenceEqualityComparer.Instance);
            public bool Wait,Fail,Retry,Skip;public int Branch,Failures,Throws;public object? Original;public readonly object?[] Saved=new object?[2];public IAsyncOnceGate? Gate;
            public static void Check(bool v,string message){if(!v)throw new InvalidOperationException(message);}
            public string Error(Exception? e)=>e==null?"none":ReferenceEquals(e,Failure)?"sentinel":e.GetType().Name;
            public void Save(int id,object r)=>Saved[id]=r;
            public void Record(object r,string phase,int value,int shared,CancellationToken t,Exception? error,SendExitInfo? exit){
                Check(t==Token.Token,"token identity");var identity=ReferenceEquals(r,Original)?"original":ReferenceEquals(r,Saved[0])?"outer":ReferenceEquals(r,Saved[1])?"inner":"replacement";
                if(phase=="K0.before")Check(identity=="original"&&value==1,"each branch starts at original generic request");
                if(phase.StartsWith("W.",StringComparison.Ordinal)||phase is "K0.after" or "K0.exception" or "K0.finally")Check(identity=="outer"&&value==11,"outer saved request remains local and stable across next/retry");
                if(phase.StartsWith("H",StringComparison.Ordinal)||phase.StartsWith("F",StringComparison.Ordinal)||phase is "K1.after" or "K1.exception" or "K1.finally")Check(identity=="inner","inner saved request identity");
                if(phase=="K1.after")Check(value is 121 or 141,"inner saved class sees handler mutation");
                Events.Add(new{phase,branch=Branch,value,shared,identity,error=Error(error),success=exit?.Success,exitError=Error(exit?.Error)});Trace.Add(phase+":"+Branch+":"+value+":"+shared+":"+identity);
            }
            public void Created(object r,string id,bool owned,bool direct){Resources.Add(r,(id,owned,direct));Trace.Add("new:"+id);}
            public void Cleanup(object r){Check(Released.Add(r),"cleanup exactly once");Trace.Add("cleanup:"+Resources[r].Id);}
            public bool FailBody(int id){if(!Fail||Branch!=2||id!=1)return false;if(!Retry||Failures++==0){Throws++;return true;}return false;}
            public ValueTask<int> BodyResponse(int value,int id){if(!Wait||id!=1)return new(value);var g=new AsyncOnceGate<int>(value);Gate=g;return g.Task;}
            public ValueTask BodyVoid(int value,int id){if(!Wait||id!=1)return default;var g=new AsyncOnceGate<int>(value);Gate=g;return g.Void;}
        }}
        """;
    private static string AllClosedHandlerWrapperConsumerCase(AllGenericCase c)
    {
        var source = AllGenericConsumerCase(c).Replace(".ReferenceAll.Run", ".GenericReferenceAll.Run", StringComparison.Ordinal);
        if (c.Name != "ARString") return source;
        const string endMarker = "interleave=new{sync=reply.A,values};}";
        var start = source.IndexOf("if(generated){dispatches++;", StringComparison.Ordinal); var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start); return source.Remove(start, end + endMarker.Length - start);
    }
    private static string AllClosedHandlerWrapperTypeCheck()
    {
        var cases = AllGenericWrapperCases.Select(c => "\"" + c.Name + "\"=>new[]{" + string.Join(",", (c.Name switch { "SRInt" => new[] { 2, 1, 1, 3 }, "SVInt" => new[] { 2, 3 }, "AVString" => new[] { 2, 1 }, _ => new[] { 2, 1, 3 } }).Select(id => "typeof(" + ClosedHandlerName(c, id) + ").ToString()")) + "}");
        return "var expectedTypes=profile switch{" + string.Join(",", cases) + ",_=>throw new InvalidOperationException(profile)};var actualTypes=p.Trace.Where(e=>e.StartsWith(\"handler-type:\",StringComparison.Ordinal)).Select(e=>e.Substring(\"handler-type:\".Length)).ToArray();P.Check(actualTypes.SequenceEqual(expectedTypes),\"actual closed request arguments and branch tag DI identity including retry and bypass\");";
    }
    private static string AllGenericWrapperSource(bool closedHandlers = false)
    {
        var common = SendAllCompositionSource(false); common = common[..common.IndexOf("namespace AllMix {", StringComparison.Ordinal)];
        var definitions = "namespace GenericCases{" + AllGenericRequestDefinition("Response", false) + AllGenericRequestDefinition("Command", true) + "}";
        return common + AllGenericWrapperProbeSource + definitions + (closedHandlers ? AllClosedHandlerDefinitions(true) : "") + string.Join("", AllGenericWrapperCases.Select(c => AllGenericModule(c, true) + (closedHandlers ? AllClosedHandlerExtras(c, true) : ""))) + """
            namespace AllGeneric{
            using P=global::GenericCases.Probe;
            using S=global::Closed.AllMixStream;using R=global::Closed.AllMixSync;
            using Zendiator.Generator.Tests;
            [ZendiatorComposition("all-generic-wrapper")]public sealed class Many;
            public static class Bridge{
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{__CONFIG__});
                private static int dispatches;
                private static async Task<(string Snapshot,object? Interleave)> Observe(string profile,bool generated){
                    var p=new P{Wait=profile=="ARString",Fail=profile is "SRInt" or "AVString",Retry=profile=="SRInt",Skip=profile=="SVInt"};var shared=new GenericCases.Shared();var single=new ManagedShapeProbe();var stream=new PublicValueRequestProbe();
                    try{
                        IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddSingleton(single);services.AddSingleton(stream);Register(services);
                        await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});await using var scope=provider.CreateAsyncScope();var sp=scope.ServiceProvider;
                        var m=sp.GetRequiredService<global::AllGeneric.Generated.Many.IZendiator>();P.Check(ReferenceEquals(m,sp.GetRequiredService<global::AllGeneric.Generated.Many.Zendiator>()),"shared mediator identity");
                        int[]? results=null;var destination=new[]{701,702,703,-9};var initial="Success";var final="Success";var error="none";object? waiting=null;object? interleave=null;var callerValue=0;var originalValue=0;
                        dispatches++;switch(profile){__CASES__}
                        P.Check(originalValue==1,"original caller generic request remains unchanged");
                        var expected=profile switch{"SRInt"=>new[]{2,1,1,3},"SVInt"=>new[]{2,3},"AVString"=>new[]{2,1},_=>new[]{2,1,3}};
                        P.Check(p.Handlers.SequenceEqual(expected)&&p.Branch==(profile=="AVString"?2:3),"branch order, retry, omitted next and propagated failure");
                        __CLOSED_TYPES__
                        if(profile=="SRInt")P.Check(results!.SequenceEqual(new[]{26,45,27})&&destination.SequenceEqual(new[]{26,45,27,-9})&&p.Throws==1,"retry succeeds and Span commits ordered complete result");
                        if(profile=="SVInt")P.Check(!p.Factories.Contains("__FACTORY_ONE__")&&p.Trace.Count(e=>e.StartsWith("K1.before:",StringComparison.Ordinal))==2&&p.Trace.Count(e=>e.StartsWith("W.skip:",StringComparison.Ordinal))==1,"omitted next does not acquire inner branch receiver");
                        if(profile=="ARString")P.Check(results!.SequenceEqual(new[]{26,25,27})&&callerValue==900&&p.Gate!.Consumptions==1,"pending single consumption across caller replacement");
                        if(profile=="AVString")P.Check(error=="sentinel"&&final=="Faulted"&&p.Throws==1&&!p.Resources.Values.Any(r=>r.Id=="H3")&&!p.Trace.Any(e=>e.StartsWith("W.caught:",StringComparison.Ordinal)),"failure escapes wrapper and stops later branch");
                        P.Check(shared.Version==(profile switch{"SRInt"=>23,"SVInt"=>15,"ARString"=>71,_=>9}),"shared reference changes across branch and retry");
                        P.Check(p.FactoryEnds.Count==0,"borrowed factory services remain until scope");
                        foreach(var resource in p.Resources)P.Check(resource.Value.Owned==p.Released.Contains(resource.Key),"operation cleans only owned resources");
                        P.Check(p.Trace.Where(e=>e.StartsWith("cleanup:",StringComparison.Ordinal)).SequenceEqual(new[]{"cleanup:H2"}),"inner owned cleanup before borrowed hooks and wrapper scope cleanup");
                        var beforeScope=p.Released.Select(r=>p.Resources[r].Id).ToArray();await scope.DisposeAsync();
                        P.Check(p.FactoryEnds.SequenceEqual(p.Factories.AsEnumerable().Reverse()),"DI factory reverse disposal order");
                        foreach(var resource in p.Resources)P.Check((resource.Value.Owned||resource.Value.Direct)==p.Released.Contains(resource.Key),"DI scope owns direct receivers; borrowed session remains external");
                        var snapshot=JsonSerializer.Serialize(new{profile,results,destination,initial,final,error,p.Branch,p.Throws,p.Handlers,__TYPE_FIELDS__p.Events,p.Trace,p.Factories,p.FactoryEnds,created=p.Resources.Values.Select(r=>new{r.Id,r.Owned,r.Direct}),released=p.Released.Select(r=>p.Resources[r].Id),beforeScope,shared=shared.Version,callerValue,originalValue,waiting,consumptions=p.Gate?.Consumptions??0});
                        foreach(var resource in p.Resources.Keys.Where(r=>!p.Released.Contains(r)).ToArray()){if(profile[0]=='A')await ((IAsyncDisposable)resource).DisposeAsync();else ((IDisposable)resource).Dispose();}
                        P.Check(p.Released.Count==p.Resources.Count,"external borrowed owner cleanup");return(snapshot,interleave);
                    }finally{p.Token.Dispose();single.Token.Dispose();}
                }
                public static async Task<string> Run(){
                    dispatches=0;var rows=new List<object>();object? interleave=null;
                    foreach(var profile in new[]{"SRInt","SVInt","ARString","AVString"}){
                        var g=await Observe(profile,true);var r=await Observe(profile,false);P.Check(g.Snapshot==r.Snapshot,"generic wrapper generated/reference mismatch "+profile+"\n"+g.Snapshot+"\n"+r.Snapshot);
                        rows.Add(new{profile,generated=JsonDocument.Parse(g.Snapshot).RootElement.Clone(),reference=JsonDocument.Parse(r.Snapshot).RootElement.Clone()});if(g.Interleave!=null)interleave=g.Interleave;
                    }
                    P.Check(dispatches==__DISPATCHES__,"four pairs and configured interleaves");return JsonSerializer.Serialize(new{generatorInputs=1,pairedScenarios=4,pairedDispatches=8,interleaveDispatches=__INTERLEAVES__,dispatches,rows,interleave});
                }
            }}
            """.Replace("__CONFIG__", (closedHandlers ? AllClosedHandlerConfiguration(AllGenericWrapperCases, true) : AllGenericConfiguration(AllGenericWrapperCases, wrapper: true)) + "c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));c.Handlers<R.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<R.Handler,R.Factory>());c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());c.Pipeline<R.Ping>(DispatchFamily.SendSync,Stage.Hook<R.Hook>());")
            .Replace("__CASES__", string.Join("", AllGenericWrapperCases.Select(c => closedHandlers ? AllClosedHandlerWrapperConsumerCase(c) : AllGenericConsumerCase(c))))
            .Replace("__CLOSED_TYPES__", closedHandlers ? AllClosedHandlerWrapperTypeCheck() : "", StringComparison.Ordinal).Replace("__TYPE_FIELDS__", closedHandlers ? "actualTypes,expectedTypes," : "", StringComparison.Ordinal)
            .Replace("__FACTORY_ONE__", closedHandlers ? "GF1" : "F1", StringComparison.Ordinal).Replace("__DISPATCHES__", closedHandlers ? "8" : "10", StringComparison.Ordinal).Replace("__INTERLEAVES__", closedHandlers ? "0" : "2", StringComparison.Ordinal);
    }
    [Fact]
    public async Task AllGenericWrapper_four_profiles_preserve_retry_bypass_pending_and_failure_propagation()
    {
        var source = AllGenericWrapperSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllGenericWrapperRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); var folder = AllGenericFolder("wrapper-runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); var contract = AssertAllGenericContract(output, driver, false, AllGenericWrapperCases);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) { File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(folder, "contract.json"), JsonSerializer.Serialize(contract)); }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = await assembly.GetType("AllGeneric.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result); using var json = JsonDocument.Parse(result); Assert.Equal(10, json.RootElement.GetProperty("dispatches").GetInt32());
    }
    [Fact]
    public async Task AllClosedHandlerWrapper_four_profiles_preserve_DI_types_retry_bypass_pending_and_failure()
    {
        var source = AllGenericWrapperSource(true); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllClosedHandlerWrapperRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); var folder = AllGenericFolder("handler-wrapper-runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); var contract = AssertAllGenericContract(output, driver, false, AllGenericWrapperCases);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) { File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(folder, "contract.json"), JsonSerializer.Serialize(contract)); }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = await assembly.GetType("AllGeneric.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result); using var json = JsonDocument.Parse(result); Assert.Equal(8, json.RootElement.GetProperty("dispatches").GetInt32()); Assert.Equal(0, json.RootElement.GetProperty("interleaveDispatches").GetInt32());
    }
}
