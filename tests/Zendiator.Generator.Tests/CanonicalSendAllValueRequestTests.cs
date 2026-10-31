using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static readonly string[] AllValueProfiles = ["SR", "SV", "AR", "AV"];
    private static bool ValueWrapper(string profile) => profile is "SR" or "AV";
    private static string ValueHandlerMode(string profile, int id) => id == 3 ? profile is "SR" or "SV" ? "direct" : profile == "AR" ? "borrowed" : "async-owned" :
        id == 1 ? profile == "SR" ? "borrowed" : profile == "AV" ? "async-borrowed" : "owned" :
        profile == "SV" ? "borrowed" : profile == "AR" ? "async-owned" : "owned";
    private static string AllValueModule(string profile)
    {
        var async = profile[0] == 'A';
        var isVoid = profile[1] == 'V';
        var wrapper = ValueWrapper(profile);
        var hookMode = wrapper ? "direct" : async ? "async-owned" : "borrowed";
        var ret = async ? isVoid ? "ValueTask" : "ValueTask<int>" : isVoid ? "void" : "int";
        var prefix = async ? "IAsyncSend" : "ISend";
        var method = async ? "HandleAsync" : "Handle";
        var disposable = async ? "IAsyncDisposable" : "IDisposable";
        string Dispose(string id) => async ? "public ValueTask DisposeAsync(){p.Cleanup(this);return default;}" : "public void Dispose()=>p.Cleanup(this);";
        string Factory(string mode, string type, string name)
        {
            if (mode == "direct")
                return "";
            var af = mode.StartsWith("async-", StringComparison.Ordinal);
            var owned = mode.EndsWith("owned", StringComparison.Ordinal);
            return $$"""
                public sealed class {{name}}:I{{(af ? "Async" : "")}}{{(owned ? "Owned" : "Borrowed")}}SessionFactory<Ping,{{type}}>,IDisposable {
                    private readonly Probe p;public {{name}}(Probe p){this.p=p;p.Factories.Add("{{profile}}.{{name}}");}
                    public {{(af ? "ValueTask<" + type + ">" : type)}} {{(af ? "CreateAsync" : "Create")}}(Ping r,CancellationToken t){r.Mark(p,"{{name}}.acquire",t);var value=new {{type}}(p);return {{(af ? "new(value)" : "value")}};}
                    public void Dispose()=>p.FactoryEnds.Add("{{profile}}.{{name}}");
                }
                """;
        }
        var text = $$"""
            namespace ValueCases.{{profile}}{
            using System;using System.Threading;using System.Threading.Tasks;using Zendiator;using Microsoft.Extensions.DependencyInjection;
            public struct Ping:{{(async ? "IRequest" : "ISyncRequest")}}{{(isVoid ? "" : "<int>")}} {
                public int Value;public long A,B,C,D,E,F,G,H;public Shared? Shared;
                public Ping(int value,Shared shared){Value=value;Shared=shared;A=101;B=202;C=303;D=404;E=505;F=606;G=707;H=808;}
                public readonly void Mark(Probe p,string phase,CancellationToken t,Exception? error=null,SendExitInfo? exit=null)=>p.Record(phase,Value,new[]{A,B,C,D,E,F,G,H},Shared?.Version??-1,t,error,exit);
                public readonly Ping Change(int value){var copy=this;copy.Value=value;if(copy.Shared!=null)copy.Shared.Version++;return copy;}
            }
            """;
        for (var hookId = 0; hookId < (wrapper ? 2 : 1); hookId++)
        {
            var id = hookId;
            var mode = hookMode;
            text += $$"""
                public sealed class Hook{{id}}:{{prefix}}RequestTransform<Ping>,{{prefix}}{{(isVoid ? "SuccessObserver<Ping>" : "ResponseTransform<Ping,int>")}},{{prefix}}ExceptionObserver<Ping>,{{prefix}}FinallyObserver<Ping>,{{disposable}}{
                    private readonly Probe p;public Hook{{id}}(Probe p){this.p=p;p.Created(this,"K{{id}}",{{(mode.EndsWith("owned", StringComparison.Ordinal) ? "true" : "false")}},{{(mode == "direct" ? "true" : "false")}});}
                    public {{(async ? "ValueTask<Ping>" : "Ping")}} {{(async ? "BeforeAsync" : "Before")}}(Ping r,CancellationToken t){ {{(id == 0 ? "p.Branch++;" : "")}}r.Mark(p,"K{{id}}.before",t);var saved=r.Change(r.Value+10);return {{(async ? "new(saved)" : "saved")}};}
                    public {{ret}} {{(async ? "AfterAsync" : "After")}}(Ping r,{{(isVoid ? "" : "int value,")}}CancellationToken t){r.Mark(p,"K{{id}}.after",t);{{(isVoid ? async ? "return default;" : "" : async ? "return new(value+1);" : "return value+1;")}}}
                    public {{(async ? "ValueTask" : "void")}} {{(async ? "OnExceptionAsync" : "OnException")}}(Ping r,Exception e,CancellationToken t){r.Mark(p,"K{{id}}.exception",t,e);{{(async ? "return default;" : "")}}}
                    public {{(async ? "ValueTask" : "void")}} {{(async ? "FinallyAsync" : "Finally")}}(Ping r,SendExitInfo e,CancellationToken t){r.Mark(p,"K{{id}}.finally",t,exit:e);{{(async ? "return default;" : "")}}}
                    {{Dispose("K" + id)}}
                }
                """;
        }
        text += Factory(hookMode, "Hook0", "KFactory");
        foreach (var id in new[] { 2, 1, 3 })
        {
            var mode = ValueHandlerMode(profile, id);
            var fail = async ? "return p." + (isVoid ? "FailedVoid" : "FailedResponse") + "();" : "throw p.Failure;";
            text += $$"""
                [ZendiatorHandler]public sealed class H{{id}}:{{(async ? "IRequestHandler" : "ISyncRequestHandler")}}<Ping{{(isVoid ? "" : ",int")}}>,{{disposable}}{
                    private readonly Probe p;public H{{id}}(Probe p){this.p=p;p.Created(this,"H{{id}}",{{(mode.EndsWith("owned", StringComparison.Ordinal) ? "true" : "false")}},{{(mode == "direct" ? "true" : "false")}});}
                    public {{ret}} {{method}}(Ping r,CancellationToken t){
                        r.Mark(p,"H{{id}}.body",t);p.Handlers.Add({{id}});if(p.FailBody({{id}})){ {{fail}} }
                        var value=r.Value+{{id}};r.Value=999;if(r.Shared!=null)r.Shared.Version+=5;r.Mark(p,"H{{id}}.local",t);
                        {{(async ? "return p." + (isVoid ? "BodyVoid" : "BodyResponse") + "(value," + id + ");" : isVoid ? "" : "return value;")}}
                    }
                    {{Dispose("H" + id)}}
                }
                """;
            text += Factory(mode, "H" + id, "F" + id);
            var completion = async ? "a" : "s";
            var reference = CanonicalCapabilityReference.Source(async, isVoid, completion, completion, completion, completion, false, hookMode, mode, wrapper);
            reference = Regex.Replace(reference, @"\bOracle\b", "Reference" + id);
            reference = Regex.Replace(reference, @"\bHandler\b", "H" + id);
            reference = Regex.Replace(reference, @"\bHFactory\b", "F" + id);
            text += reference.Replace("namespace App {", "", StringComparison.Ordinal)[..^1];
        }
        if (wrapper)
        {
            var aw = async ? "await " : "";
            var cfg = async ? ".ConfigureAwait(false)" : "";
            var invoke = async ? "InvokeAsync" : "Invoke";
            var value = isVoid ? "" : "value=";
            text += $$"""
                public sealed class W:{{prefix}}Wrapper<Ping{{(isVoid ? "" : ",int")}}>,{{disposable}}{
                    private readonly Probe p;public W(Probe p){this.p=p;p.Created(this,"W",false,true);}
                    public {{(async ? "async " : "")}}{{ret}} {{invoke}}<N>(Ping r,N next,CancellationToken t)where N:{{(async ? "IAsyncSendContinuation" : "ISendContinuation")}}<Ping{{(isVoid ? "" : ",int")}}>{
                        r.Mark(p,"W.before",t);{{(isVoid ? "" : "int value;")}}
                        try{ {{value}}{{aw}}next.{{invoke}}(r,t){{cfg}}; }
                        catch(Exception e)when(ReferenceEquals(e,p.Failure)){r.Mark(p,"W.caught",t,e);{{value}}{{aw}}next.{{invoke}}(r.Change(r.Value+20),t){{cfg}};}
                        r.Mark(p,"W.after",t);{{(isVoid ? "" : "return value+1;")}}
                    }
                    {{Dispose("W")}}
                }
                """;
        }
        var allRet = async ? isVoid ? "ValueTask" : "ValueTask<int[]>" : isVoid ? "void" : "int[]";
        var calls = string.Join("", new[] { 2, 1, 3 }.Select((id, i) => (isVoid ? "" : "results[" + i + "]=") + (async ? "await " : "") + "Reference" + id + ".Send(p,r,t)" + (async ? ".ConfigureAwait(false)" : "") + ";"));
        text += "public static class ReferenceAll{public static " + allRet + " Run(IServiceProvider p,Ping r,CancellationToken t){" +
            (async ? "return Execute(p,r,t);}private static async " + allRet + " Execute(IServiceProvider p,Ping r,CancellationToken t){" : "") +
            "t.ThrowIfCancellationRequested();" + (isVoid ? "" : "var results=new int[3];") + calls + (isVoid ? "" : "return results;") + "}}}";
        return text;
    }
    private const string AllValueProbeSource = """
        namespace ValueCases{
        using Zendiator.Generator.Tests;
        public sealed class Shared{public int Version;}
        public sealed class Probe{
            public readonly CancellationTokenSource Token=new();public readonly Exception Failure=new FormatException("first-next");
            public readonly List<object> Events=new();public readonly List<int> Handlers=new();public readonly List<string> Factories=new(),FactoryEnds=new(),Trace=new();
            public readonly Dictionary<object,(string Id,bool Owned,bool Direct)> Resources=new(ReferenceEqualityComparer.Instance);
            public readonly HashSet<object> Released=new(ReferenceEqualityComparer.Instance);public readonly List<IAsyncOnceGate> FailedGates=new();
            public string Profile="";public bool Default,Wait;public int Branch,Failures;public IAsyncOnceGate? Gate;
            public static void Check(bool v,string message){if(!v)throw new InvalidOperationException(message);}
            public void Record(string phase,int value,long[] fields,int shared,CancellationToken t,Exception? error,SendExitInfo? exit){
                Check(t==Token.Token,"token identity");Check(fields.SequenceEqual(Default?new long[8]:new long[]{101,202,303,404,505,606,707,808}),"large saved struct fields");
                Events.Add(new{phase,branch=Branch,value,fields,shared,error=Error(error),success=exit?.Success,exitError=Error(exit?.Error)});Trace.Add(phase+":"+Branch+":"+value+":"+shared);
                if(phase=="K0.before")Check(value==(Default?0:1),"branch starts with original value snapshot");
                if(phase.EndsWith(".after",StringComparison.Ordinal))Check(value!=999,"handler local mutation must not overwrite hook saved value");
            }
            public string Error(Exception? e)=>e==null?"none":ReferenceEquals(e,Failure)?"sentinel":e.GetType().Name;
            public void Created(object value,string id,bool owned,bool direct){Resources.Add(value,(id,owned,direct));Trace.Add("new:"+id);}
            public void Cleanup(object value){Check(Released.Add(value),"resource cleanup twice");Trace.Add("cleanup:"+Resources[value].Id);}
            public bool FailBody(int id)=>Profile is "SR" or "AV"&&Branch==2&&id==1&&Failures++==0;
            public ValueTask<int> FailedResponse(){var g=new AsyncOnceGate<int>(0);FailedGates.Add(g);g.Fail(Failure);return g.Task;}
            public ValueTask FailedVoid(){var g=new AsyncOnceGate<int>(0);FailedGates.Add(g);g.Fail(Failure);return g.Void;}
            public ValueTask<int> BodyResponse(int value,int id){if(!Wait||id!=1)return new(value);var g=new AsyncOnceGate<int>(value);Gate=g;return g.Task;}
            public ValueTask BodyVoid(int value,int id){if(!Wait||id!=1)return default;var g=new AsyncOnceGate<int>(value);Gate=g;return g.Void;}
        }}
        """;
    private static string AllValueConfiguration()
    {
        var handlers = new List<string>();
        var pipelines = new List<string>();
        foreach (var (profile, alias) in AllValueProfiles.Zip(new[] { "T", "U", "V", "W" }))
        {
            var family = profile[0] == 'A' ? "SendAllAsync" : "SendAllSync";
            var wrapper = ValueWrapper(profile);
            var entries = new[] { 2, 1, 3 }.Select(id => ValueHandlerMode(profile, id) == "direct" ? $"typeof({alias}.H{id})" : $"HandlerEntry.Session<{alias}.H{id},{alias}.F{id}>()");
            handlers.Add($"c.Handlers<{alias}.Ping>(DispatchFamily.{family},{string.Join(",", entries)});");
            pipelines.Add($"c.Pipeline<{alias}.Ping>(DispatchFamily.{family},Stage.Hook<{alias}.Hook0{(wrapper ? "" : "," + alias + ".KFactory")}>(){(wrapper ? ",Stage.Wrapper<" + alias + ".W>(),Stage.Hook<" + alias + ".Hook1>()" : "")});");
        }
        return string.Join("", handlers.Take(2)) + "c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));c.Handlers<A.Ping>(DispatchFamily.SendAsync,HandlerEntry.Session<A.Handler,A.Factory>());" +
            string.Join("", handlers.Skip(2)) + "c.Handlers<R.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<R.Handler,R.Factory>());" +
            string.Join("", pipelines.AsEnumerable().Reverse()) + "c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());c.Pipeline<A.Ping>(DispatchFamily.SendAsync,Stage.Hook<A.Hook>());c.Pipeline<R.Ping>(DispatchFamily.SendSync,Stage.Hook<R.Hook>());";
    }
    private static string AllValueConsumerCase(string profile)
    {
        var alias = profile switch
        {
            "SR" => "T",
            "SV" => "U",
            "AR" => "V",
            _ => "W"
        };
        var async = profile[0] == 'A';
        var isVoid = profile[1] == 'V';
        var request = profile == "SV" ? "default(" + alias + ".Ping)" : "new " + alias + ".Ping(1,shared)";
        var call = (async ? "task=" : isVoid ? "" : "results=") + "(generated?" + "m." + (async ? "SendAllAsync" : "SendAll") + "(request,p.Token.Token):" + alias + ".ReferenceAll.Run(sp,request,p.Token.Token));";
        if (!async && isVoid)
            call = "if(generated)m.SendAll(request,p.Token.Token);else " + alias + ".ReferenceAll.Run(sp,request,p.Token.Token);";
        if (profile == "SR")
            call = "if(generated){var count=m.SendAll(request,destination,p.Token.Token);P.Check(count==3&&destination[3]==-9,\"Span complete commit/trailing sentinel\");results=destination.Take(3).ToArray();}else{results=T.ReferenceAll.Run(sp,request,p.Token.Token);results.CopyTo(destination,0);}";
        return $$"""
            case "{{profile}}":{
                var request={{request}};{{(async ? (isVoid ? "ValueTask" : "ValueTask<int[]>") + " task;" : "")}}
                {{call}}
                {{(async ? "initial=task.IsCompletedSuccessfully?\"Success\":task.IsFaulted?\"Faulted\":task.IsCanceled?\"Canceled\":\"Pending\";" : "")}}
                {{(profile == "AR" ? """
                    var gate=p.Gate!;P.Check(initial=="Pending"&&p.Branch==2&&gate.Consumptions==0,"large value await boundary");
                    P.Check(!p.Factories.Contains("AR.F3")&&!p.Resources.Values.Any(r=>r.Id=="H3"),"later branch not resolved while pending");
                    waiting=new{p.Branch,events=p.Events.ToArray(),trace=p.Trace.ToArray(),consumptions=gate.Consumptions};
                    request.Value=900;request.A=-900;shared.Version+=50;callerValue=request.Value;callerA=request.A;
                    if(generated){dispatches++;var reply=m.Send(new R.Ping(10),single.Token.Token);P.Check(reply.A==12,"Sync interleave");var values=new List<int>();dispatches++;await foreach(var item in m.StreamAsync(new S.Items(new S.SharedState())))values.Add(item);P.Check(values.SequenceEqual(new[]{2,4})&&stream.SourceDisposals==1,"Stream interleave");P.Check(p.Branch==2&&gate.Consumptions==0&&!task.IsCompleted,"interleave preserves pending All value state");interleave=new{sync=reply.A,values};}
                    gate.Complete();
                    """ : "")}}
                {{(async ? (isVoid ? "await task;" : "results=await task;") + "final=task.IsCompletedSuccessfully?\"Success\":task.IsCanceled?\"Canceled\":\"Faulted\";" : "")}}
                {{(profile == "AR" ? "" : "callerValue=request.Value;callerA=request.A;")}}
                break;
            }
            """;
    }
    private static string AllValueSource(bool gate)
    {
        var common = SendAllCompositionSource(false);
        common = common[..common.IndexOf("namespace AllMix {", StringComparison.Ordinal)];
        return common + AllValueProbeSource + string.Join("", AllValueProfiles.Select(AllValueModule)) + """
            namespace AllValue{
            using System;using System.Collections.Generic;using System.Linq;using System.Text.Json;using System.Threading;using System.Threading.Tasks;
            using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;using Zendiator.Generator.Tests;
            using T=global::ValueCases.SR;using U=global::ValueCases.SV;using V=global::ValueCases.AR;using W=global::ValueCases.AV;using P=global::ValueCases.Probe;
            using S=global::Closed.AllMixStream;using R=global::Closed.AllMixSync;using A=global::Closed.AllMixAsync;
            [ZendiatorComposition("all-values")]public sealed class Many;
            public static class Bridge{
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{__CONFIG__});
                __CONSUMER__
            }}
            """.Replace("__CONFIG__", AllValueConfiguration()).Replace("__CONSUMER__", gate ? "" : """
                private static int dispatches;
                private static async Task<(string Snapshot,object? Interleave)> Observe(string profile,bool generated){
                    var p=new P{Profile=profile,Default=profile=="SV",Wait=profile=="AR"};var shared=new ValueCases.Shared();var single=new ManagedShapeProbe();var stream=new PublicValueRequestProbe();
                    try{
                        IServiceCollection defaults=new ServiceCollection();Register(defaults);
                        IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddSingleton(single);services.AddSingleton(stream);
                        foreach(var d in defaults.Where(d=>d.ServiceType.Namespace?.StartsWith("ValueCases.",StringComparison.Ordinal)==true)){P.Check(d.Lifetime==ServiceLifetime.Transient&&d.ImplementationType==d.ServiceType,"default callback/factory registration");services.AddScoped(d.ServiceType,d.ImplementationType!);}
                        Register(services);
                        await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});await using var scope=provider.CreateAsyncScope();var sp=scope.ServiceProvider;
                        var m=sp.GetRequiredService<global::AllValue.Generated.Many.IZendiator>();P.Check(ReferenceEquals(m,sp.GetRequiredService<global::AllValue.Generated.Many.IZendiator>()),"shared mediator identity");
                        int[]? results=null;var destination=new[]{701,702,703,-9};var initial="Success";var final="Success";object? waiting=null;object? interleave=null;var callerValue=0;long callerA=0;
                        dispatches++;switch(profile){__CASES__}
                        P.Check(p.Branch==3,"all original branch values preserved");P.Check(p.Handlers.SequenceEqual(profile is "SR" or "AV"?new[]{2,1,1,3}:new[]{2,1,3}),"branch order and retry call count");
                        if(profile=="SR")P.Check(results!.SequenceEqual(new[]{26,45,27})&&shared.Version==23,"sync value retry replacement and shared changes");
                        if(profile=="AR")P.Check(results!.SequenceEqual(new[]{14,13,15})&&shared.Version==68&&callerValue==900&&callerA==-900&&p.Gate!.Consumptions==1,"large value saved across await and caller mutation");
                        if(profile=="AV")P.Check(shared.Version==23&&p.FailedGates.Single().Consumptions==1,"async failed next consumed once then retry succeeds");
                        if(profile=="SV")P.Check(callerValue==0&&callerA==0,"actual default value caller unchanged");
                        P.Check(p.FactoryEnds.Count==0,"factories borrowed until scope");
                        foreach(var resource in p.Resources)P.Check(resource.Value.Owned==p.Released.Contains(resource.Key),"only owned operation cleanup");
                        var beforeScope=p.Released.Select(r=>p.Resources[r].Id).ToArray();await scope.DisposeAsync();
                        P.Check(p.FactoryEnds.SequenceEqual(p.Factories.AsEnumerable().Reverse()),"DI factory ownership/order");
                        foreach(var resource in p.Resources)P.Check((resource.Value.Owned||resource.Value.Direct)==p.Released.Contains(resource.Key),"direct DI cleanup vs borrowed session");
                        var snapshot=JsonSerializer.Serialize(new{profile,results,destination,initial,final,p.Branch,p.Handlers,p.Events,p.Trace,p.Factories,p.FactoryEnds,created=p.Resources.Values.Select(r=>new{r.Id,r.Owned,r.Direct}),released=p.Released.Select(r=>p.Resources[r].Id),beforeScope,shared=shared.Version,callerValue,callerA,waiting,consumptions=p.Gate?.Consumptions??0,failedConsumptions=p.FailedGates.Select(g=>g.Consumptions)});
                        foreach(var resource in p.Resources.Keys.Where(r=>!p.Released.Contains(r)).ToArray()){if(profile[0]=='A')await ((IAsyncDisposable)resource).DisposeAsync();else ((IDisposable)resource).Dispose();}
                        P.Check(p.Released.Count==p.Resources.Count,"external borrowed owner cleanup");return(snapshot,interleave);
                    }finally{p.Token.Dispose();single.Token.Dispose();}
                }
                public static async Task<string> Run(){
                    dispatches=0;var rows=new List<object>();object? interleave=null;
                    foreach(var profile in new[]{"SR","SV","AR","AV"}){
                        var g=await Observe(profile,true);var r=await Observe(profile,false);P.Check(g.Snapshot==r.Snapshot,"value generated/reference mismatch "+profile+"\n"+g.Snapshot+"\n"+r.Snapshot);
                        rows.Add(new{profile,generated=JsonDocument.Parse(g.Snapshot).RootElement.Clone(),reference=JsonDocument.Parse(r.Snapshot).RootElement.Clone()});if(g.Interleave!=null)interleave=g.Interleave;
                    }
                    P.Check(dispatches==10,"four pairs and two interleaves");return JsonSerializer.Serialize(new{generatorInputs=1,pairedScenarios=4,pairedDispatches=8,interleaveDispatches=2,dispatches,rows,interleave});
                }
                """.Replace("__CASES__", string.Join("", AllValueProfiles.Select(AllValueConsumerCase))));
    }
    private static object AssertAllValueContract(Compilation output, GeneratorDriver driver)
    {
        foreach (var profile in AllValueProfiles)
            Assert.True(output.GetTypeByMetadataName("ValueCases." + profile + ".Ping")!.IsValueType);
        var api = output.GetTypeByMetadataName("AllValue.Generated.Many.IZendiator")!;
        Assert.NotNull(api);
        var all = api.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name is "SendAll" or "SendAllAsync").ToArray();
        Assert.Equal(5, all.Length);
        Assert.All(all, m => Assert.True(m.Parameters[0].Type.IsValueType));
        var methods = new List<string>();
        var guards = 0;
        foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources.Where(IsCanonicalRoute))
            foreach (var method in CSharpSyntaxTree.ParseText(file.SourceText).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m => m.ParameterList.Parameters.Any(p => p.Type?.ToString().StartsWith("global::ValueCases.", StringComparison.Ordinal) == true)))
            {
                methods.Add(method.Identifier.ValueText);
                var boxingGuards = method.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => i.Expression.ToString().EndsWith("ArgumentNullException.ThrowIfNull", StringComparison.Ordinal) && i.ArgumentList.Arguments.Any(a => a.Expression is IdentifierNameSyntax n && (n.Identifier.ValueText == "request" || n.Identifier.ValueText.StartsWith("saved", StringComparison.Ordinal)))).ToArray();
                guards += boxingGuards.Length;
                Assert.Empty(boxingGuards);
                Assert.DoesNotContain(method.DescendantNodes().OfType<CastExpressionSyntax>(), c => c.Type.ToString() is "object" or "global::System.Object");
            }
        Assert.True(methods.Count >= 17);
        return new
        {
            typedValueMethods = methods,
            requestBoxingNullGuards = guards,
            allPublicMethods = all.Length
        };
    }
    private static string? AllValueFolder(string name)
    {
        var e = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(e))
            return null;
        var p = Path.Combine(e, "all-value-" + name);
        Directory.CreateDirectory(p);
        return p;
    }
    [Fact]
    public void AllValue_gate_preserves_typed_all_response_void_and_omits_boxing_null_guards()
    {
        var source = AllValueSource(true);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllValueGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = AllValueFolder("gate");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var contract = AssertAllValueContract(output, driver);
        if (folder != null)
            File.WriteAllText(Path.Combine(folder, "gate-result.json"), JsonSerializer.Serialize(new
            {
                generatorInputs = 1,
                dispatches = 0,
                contract,
                profiles = AllValueProfiles
            }));
    }
    [Fact]
    public async Task AllValue_four_profiles_preserve_default_large_saved_values_shared_references_and_wrapper_retry()
    {
        var source = AllValueSource(false);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllValueRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var folder = AllValueFolder("runtime");
        SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var contract = AssertAllValueContract(output, driver);
        using var bytes = new MemoryStream();
        var emitted = output.Emit(bytes);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "contract.json"), JsonSerializer.Serialize(contract));
        }
        bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var result = await assembly.GetType("AllValue.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null)
            File.WriteAllText(Path.Combine(folder, "runtime.json"), result);
        using var json = JsonDocument.Parse(result);
        Assert.Equal(10, json.RootElement.GetProperty("dispatches").GetInt32());
    }
}

