using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;
using Zendiator.SourceGenerator;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record AllGenericCase(string Name, bool Async, bool Void, string Argument, bool Legacy = false)
    {
        public string Request => Legacy ? "global::GenericCases.LegacyResponse" : "global::GenericCases." + (Void ? "Command" : "Response") + "<" + Argument + ">";
        public string Family => Async ? "SendAllAsync" : "SendAllSync";
    }
    private static readonly AllGenericCase[] AllGenericCases =
    [
        new("SRInt", false, false, "int"), new("SRString", false, false, "string"),
        new("SVInt", false, true, "int"), new("SVString", false, true, "string"),
        new("ARInt", true, false, "int"), new("ARString", true, false, "string"),
        new("AVInt", true, true, "int"), new("AVString", true, true, "string")
    ];
    private static readonly string[] AllGenericRuntimeNames = ["SRInt", "SRString", "SVInt", "ARString", "AVInt", "AVString"];
    private static string AllGenericHandlerMode(AllGenericCase c, int id) => id == 3 ? "direct" : id == 1 ? c.Async ? "async-borrowed" : "borrowed" : c.Async ? "async-owned" : "owned";
    private static string AllGenericHookMode(AllGenericCase c) => c.Async ? "async-owned" : "owned";
    private static string AllGenericRequestDefinition(string name, bool isVoid, bool legacy = false) => $$"""
        public sealed class {{name}}{{(legacy ? "" : "<T>")}}:{{(isVoid ? "ISyncRequest,IRequest" : "ISyncRequest<int>,IRequest<int>")}}{
            public int Value;public readonly Shared Shared;
            public {{name}}(int value,Shared shared){Value=value;Shared=shared;}
            public {{name}}{{(legacy ? "" : "<T>")}} Change(int value){Shared.Version++;return new(value,Shared);}
            public void Mark(Probe p,string phase,CancellationToken t,Exception? error=null,SendExitInfo? exit=null)=>p.Record(this,phase,Value,Shared.Version,t,error,exit);
        }
        """;
    private static string AllGenericModule(AllGenericCase c, bool wrapper = false)
    {
        var async = c.Async; var isVoid = c.Void; var hookMode = wrapper ? "direct" : AllGenericHookMode(c);
        var ret = async ? isVoid ? "ValueTask" : "ValueTask<int>" : isVoid ? "void" : "int";
        var prefix = async ? "IAsyncSend" : "ISend"; var disposable = async ? "IAsyncDisposable" : "IDisposable";
        var dispose = async ? "public ValueTask DisposeAsync(){p.Cleanup(this);return default;}" : "public void Dispose()=>p.Cleanup(this);";
        string Factory(string mode, string type, string name)
        {
            if (mode == "direct") return "";
            var af = mode.StartsWith("async-", StringComparison.Ordinal); var owned = mode.EndsWith("owned", StringComparison.Ordinal);
            return $$"""
                public class {{name}}:I{{(af ? "Async" : "")}}{{(owned ? "Owned" : "Borrowed")}}SessionFactory<Ping,{{type}}>,IDisposable{
                    private readonly Probe p;public {{name}}(Probe p){this.p=p;p.Factories.Add("{{name}}");}
                    public {{(af ? "ValueTask<" + type + ">" : type)}} {{(af ? "CreateAsync" : "Create")}}(Ping r,CancellationToken t){r.Mark(p,"{{name}}.acquire",t);var value=new {{type}}(p);return {{(af ? "new(value)" : "value")}};}
                    public void Dispose()=>p.FactoryEnds.Add("{{name}}");
                }
                public sealed class Generic{{name}}<T>:{{name}}{public Generic{{name}}(Probe p):base(p){} }
                """;
        }
        var text = $$"""
            namespace GenericCases.{{c.Name}}{
            using Ping={{c.Request}};
            public class Hook0:{{prefix}}RequestTransform<Ping>,{{prefix}}{{(isVoid ? "SuccessObserver<Ping>" : "ResponseTransform<Ping,int>")}},{{prefix}}ExceptionObserver<Ping>,{{prefix}}FinallyObserver<Ping>,{{disposable}}{
                private readonly Probe p;public Hook0(Probe p){this.p=p;p.Created(this,"K0",{{(wrapper ? "false,true" : "true,false")}});}
                public {{(async ? "ValueTask<Ping>" : "Ping")}} {{(async ? "BeforeAsync" : "Before")}}(Ping r,CancellationToken t){p.Branch++;r.Mark(p,"K0.before",t);var saved=r.Change(r.Value+10);{{(wrapper ? "p.Save(0,saved);" : "p.Saved=saved;")}}return {{(async ? "new(saved)" : "saved")}};}
                public {{ret}} {{(async ? "AfterAsync" : "After")}}(Ping r,{{(isVoid ? "" : "int value,")}}CancellationToken t){r.Mark(p,"K0.after",t);{{(isVoid ? async ? "return default;" : "" : async ? "return new(value+1);" : "return value+1;")}}}
                public {{(async ? "ValueTask" : "void")}} {{(async ? "OnExceptionAsync" : "OnException")}}(Ping r,Exception e,CancellationToken t){r.Mark(p,"K0.exception",t,e);{{(async ? "return default;" : "")}}}
                public {{(async ? "ValueTask" : "void")}} {{(async ? "FinallyAsync" : "Finally")}}(Ping r,SendExitInfo e,CancellationToken t){r.Mark(p,"K0.finally",t,exit:e);{{(async ? "return default;" : "")}}}
                {{dispose}}
            }
            public sealed class GenericHook<T>:Hook0{public GenericHook(Probe p):base(p){} }
            public sealed class BeforeOnly:{{prefix}}RequestTransform<Ping>{public {{(async ? "ValueTask<Ping> BeforeAsync" : "Ping Before")}}(Ping r,CancellationToken t)=>{{(async ? "new(r)" : "r")}};}
            public sealed class MixedFull:{{prefix}}RequestTransform<Ping>,{{prefix}}{{(isVoid ? "SuccessObserver<Ping>" : "ResponseTransform<Ping,int>")}},{{prefix}}ExceptionObserver<Ping>,{{(async ? "ISend" : "IAsyncSend")}}FinallyObserver<Ping>{
                public {{(async ? "ValueTask<Ping> BeforeAsync" : "Ping Before")}}(Ping r,CancellationToken t)=>{{(async ? "new(r)" : "r")}};
                public {{ret}} {{(async ? "AfterAsync" : "After")}}(Ping r,{{(isVoid ? "" : "int value,")}}CancellationToken t){ {{(isVoid ? async ? "return default;" : "" : async ? "return new(value);" : "return value;")}} }
                public {{(async ? "ValueTask OnExceptionAsync" : "void OnException")}}(Ping r,Exception e,CancellationToken t){ {{(async ? "return default;" : "")}} }
                public {{(async ? "void Finally" : "ValueTask FinallyAsync")}}(Ping r,SendExitInfo e,CancellationToken t){ {{(async ? "" : "return default;")}} }
            }
            public class W:{{prefix}}Wrapper<Ping{{(isVoid ? "" : ",int")}}>{public {{ret}} {{(async ? "InvokeAsync" : "Invoke")}}<N>(Ping r,N next,CancellationToken t)where N:{{(async ? "IAsyncSendContinuation" : "ISendContinuation")}}<Ping{{(isVoid ? "" : ",int")}}> { {{(isVoid && !async ? "next.Invoke(r,t);" : "return next." + (async ? "InvokeAsync" : "Invoke") + "(r,t);")}} } }
            public sealed class GenericW<T>:W{ {{(wrapper ? "public GenericW(Probe p):base(p){}" : "")}} }
            public sealed class OtherW:W{ {{(wrapper ? "public OtherW(Probe p):base(p){}" : "")}} }
            """ + Factory(hookMode, "Hook0", "KFactory");
        if (wrapper)
        {
            var start = text.IndexOf("public class Hook0:", StringComparison.Ordinal); var end = text.IndexOf("public sealed class GenericHook", start, StringComparison.Ordinal);
            var inner = text[start..end].Replace("Hook0", "Hook1", StringComparison.Ordinal).Replace("K0", "K1", StringComparison.Ordinal).Replace("p.Branch++;", "", StringComparison.Ordinal).Replace("p.Save(0,saved)", "p.Save(1,saved)", StringComparison.Ordinal);
            text = text.Insert(end, inner);
            start = text.IndexOf("public class W:", StringComparison.Ordinal); end = text.IndexOf("public sealed class GenericW", start, StringComparison.Ordinal);
            text = text[..start] + AllGenericWrapperDeclaration(c) + text[end..];
        }
        foreach (var id in new[] { 2, 1, 3 })
        {
            var mode = AllGenericHandlerMode(c, id);
            text += $$"""
                [ZendiatorHandler]public class H{{id}}:{{(async ? "IRequestHandler" : "ISyncRequestHandler")}}<Ping{{(isVoid ? "" : ",int")}}>,{{disposable}}{
                    private readonly Probe p;public H{{id}}(Probe p){this.p=p;p.Created(this,"H{{id}}",{{(mode.EndsWith("owned", StringComparison.Ordinal) ? "true" : "false")}},{{(mode == "direct" ? "true" : "false")}});}
                    public {{ret}} {{(async ? "HandleAsync" : "Handle")}}(Ping r,CancellationToken t){
                        r.Mark(p,"H{{id}}.body",t);p.Handlers.Add({{id}});if({{(wrapper ? "p.FailBody(" + id + ")" : "p.Fail&&" + id + "==1")}})throw p.Failure;
                        var value=r.Value+{{id}};r.Value+=100;r.Shared.Version+=5;r.Mark(p,"H{{id}}.local",t);
                        {{(async ? "return p." + (isVoid ? "BodyVoid" : "BodyResponse") + "(value," + id + ");" : isVoid ? "" : "return value;")}}
                    }
                    {{dispose}}
                }
                """ + Factory(mode, "H" + id, "F" + id);
            var completion = async ? "a" : "s";
            var reference = CanonicalCapabilityReference.Source(async, isVoid, completion, completion, completion, completion, false, hookMode, mode, wrapper);
            reference = Regex.Replace(reference, @"\bOracle\b", "Reference" + id); reference = Regex.Replace(reference, @"\bHandler\b", "H" + id); reference = Regex.Replace(reference, @"\bHFactory\b", "F" + id);
            text += reference.Replace("namespace App {", "", StringComparison.Ordinal)[..^1];
        }
        text += "[ZendiatorHandler]public sealed class GenericH3<T>:H3{public GenericH3(Probe p):base(p){}}";
        var allRet = async ? isVoid ? "ValueTask" : "ValueTask<int[]>" : isVoid ? "void" : "int[]";
        var calls = string.Join("", new[] { 2, 1, 3 }.Select((id, i) => (isVoid ? "" : "results[" + i + "]=") + (async ? "await " : "") + "Reference" + id + ".Send(p,r,t)" + (async ? ".ConfigureAwait(false)" : "") + ";"));
        return text + "public static class ReferenceAll{public static " + (async ? "async " : "") + allRet + " Run(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);t.ThrowIfCancellationRequested();" + (isVoid ? "" : "var results=new int[3];") + calls + (isVoid ? "" : "return results;") + "}}}";
    }
    private const string AllGenericProbeSource = """
        namespace GenericCases{
        using Zendiator.Generator.Tests;
        public sealed class Shared{public int Version;}
        public sealed class Probe{
            public readonly CancellationTokenSource Token=new();public readonly Exception Failure=new FormatException("planned");
            public readonly List<object> Events=new();public readonly List<int> Handlers=new();public readonly List<string> Factories=new(),FactoryEnds=new(),Trace=new();
            public readonly Dictionary<object,(string Id,bool Owned,bool Direct)> Resources=new(ReferenceEqualityComparer.Instance);
            public readonly HashSet<object> Released=new(ReferenceEqualityComparer.Instance);
            public bool Wait,Fail;public int Branch;public object? Original,Saved;public IAsyncOnceGate? Gate;
            public static void Check(bool v,string message){if(!v)throw new InvalidOperationException(message);}
            public string Error(Exception? e)=>e==null?"none":ReferenceEquals(e,Failure)?"sentinel":e.GetType().Name;
            public void Record(object r,string phase,int value,int shared,CancellationToken t,Exception? error,SendExitInfo? exit){
                Check(t==Token.Token,"token identity");var identity=ReferenceEquals(r,Original)?"original":ReferenceEquals(r,Saved)?"saved":"other";
                if(phase=="K0.before")Check(identity=="original"&&value==1,"each branch receives original closed generic object");
                if(phase.StartsWith("H",StringComparison.Ordinal)||phase is "K0.after" or "K0.exception" or "K0.finally")Check(identity=="saved","replacement remains local to branch");
                if(phase=="K0.after")Check(value==111,"saved class reference observes handler mutation");
                Events.Add(new{phase,branch=Branch,value,shared,identity,error=Error(error),success=exit?.Success,exitError=Error(exit?.Error)});Trace.Add(phase+":"+Branch+":"+value+":"+shared+":"+identity);
            }
            public void Created(object r,string id,bool owned,bool direct){Resources.Add(r,(id,owned,direct));Trace.Add("new:"+id);}
            public void Cleanup(object r){Check(Released.Add(r),"cleanup exactly once");Trace.Add("cleanup:"+Resources[r].Id);}
            public ValueTask<int> BodyResponse(int value,int id){if(!Wait||id!=1)return new(value);var g=new AsyncOnceGate<int>(value);Gate=g;return g.Task;}
            public ValueTask BodyVoid(int value,int id){if(!Wait||id!=1)return default;var g=new AsyncOnceGate<int>(value);Gate=g;return g.Void;}
        }}
        """;
    private static string AllGenericConfiguration(IEnumerable<AllGenericCase> cases, string mutation = "", bool wrapper = false)
    {
        var handlers = new List<string>(); var pipelines = new List<string>();
        foreach (var c in cases)
        {
            var a = "global::GenericCases." + c.Name + ".";
            var entries = new[] { 2, 1, 3 }.Select(id => id == 3 ? "typeof(" + a + (mutation is "handler" or "handlerWrapper" ? "GenericH3<string>" : "H3") + ")" : "HandlerEntry.Session<" + a + "H" + id + "," + a + (mutation == "factory" && id == 1 ? "GenericF1<string>" : "F" + id) + ">()");
            handlers.Add($"c.Handlers<{c.Request}>(DispatchFamily.{c.Family},{string.Join(",", entries)});");
            var stage = mutation switch
            {
                "hook" or "hookWrapper" => "Stage.Hook<" + a + "GenericHook<string>>()",
                "hookFactory" => "Stage.Hook<" + a + "Hook0," + a + "GenericKFactory<string>>()",
                "before" or "partialWrapper" => "Stage.Hook<" + a + "BeforeOnly>()",
                "mixedWrapper" => "Stage.Hook<" + a + "MixedFull>()",
                "onlyWrapper" => "Stage.Wrapper<" + a + "W>()",
                _ => "Stage.Hook<" + a + "Hook0" + (wrapper ? "" : "," + a + "KFactory") + ">()"
            };
            if (mutation is "wrapper" or "genericWrapper" or "handlerWrapper" or "hookWrapper" or "partialWrapper" or "mixedWrapper" or "multipleWrappers") stage += ",Stage.Wrapper<" + a + (mutation == "genericWrapper" ? "GenericW<string>" : "W") + ">()";
            if (mutation == "multipleWrappers") stage += ",Stage.Wrapper<" + a + "OtherW>()";
            if (wrapper) stage += ",Stage.Wrapper<" + a + "W>(),Stage.Hook<" + a + "Hook1>()";
            pipelines.Add($"c.Pipeline<{c.Request}>(DispatchFamily.{c.Family},{stage});");
        }
        return string.Join("", handlers) + string.Join("", pipelines.AsEnumerable().Reverse());
    }
    private static string AllGenericRejectedSource()
    {
        var text = "";
        foreach (var c in new[] { AllGenericCases[0], AllGenericCases[5] })
            foreach (var mutation in new[] { "handler", "handlerWrapper", "hook", "hookWrapper", "factory", "hookFactory", "wrapper", "genericWrapper", "before", "default", "partialWrapper", "mixedWrapper", "onlyWrapper", "multipleWrappers" })
            {
                if (mutation == "default" && c.Async) continue;
                var marker = "Reject" + c.Name + mutation;
                text += (mutation == "default" ? "" : "[ZendiatorComposition(\"" + marker + "\")]public sealed class " + marker + ";") +
                    "public static class " + marker + "Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator" + (mutation == "default" ? "" : "<" + marker + ">") + "(c=>{" + AllGenericConfiguration([c], mutation) + "});}";
            }
        var whole = AllGenericConfiguration(AllGenericCases, "wrapper").Replace("typeof(global::GenericCases.SRInt.H3)", "typeof(global::GenericCases.SRInt.GenericH3<string>)", StringComparison.Ordinal)
            .Replace("global::GenericCases.SRInt.H1,global::GenericCases.SRInt.F1>", "global::GenericCases.SRInt.H1,global::GenericCases.SRInt.GenericF1<string>>", StringComparison.Ordinal).Replace("Stage.Hook<global::GenericCases.SRInt.Hook0,global::GenericCases.SRInt.KFactory>()", "Stage.Hook<global::GenericCases.SRInt.BeforeOnly>()", StringComparison.Ordinal);
        text += "[ZendiatorComposition(\"whole-generic-boundary\")]public sealed class RejectWhole;public static class RejectWholeBridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<RejectWhole>(c=>{" + whole + "});}";
        foreach (var mutation in new[] { "handler", "hook", "factory", "hookFactory" })
        {
            var c = new AllGenericCase("Legacy", false, false, "int", true);
            text += "[ZendiatorComposition(\"legacy-" + mutation + "\")]public sealed class Legacy" + mutation + ";public static class Legacy" + mutation + "Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Legacy" + mutation + ">(c=>{" + AllGenericConfiguration([c], mutation) + "});}";
        }
        return text + AllHandlerVisibilityCases() + AllClosedHookGateCases() + AllGenericCompositionGateCases();
    }
    private static string AllGenericConsumerCase(AllGenericCase c)
    {
        var a = "global::GenericCases." + c.Name + ".";
        var call = c.Async ? "task=generated?m.SendAllAsync(request,p.Token.Token):" + a + "ReferenceAll.Run(sp,request,p.Token.Token);" : c.Void ? "if(generated)m.SendAll(request,p.Token.Token);else " + a + "ReferenceAll.Run(sp,request,p.Token.Token);" : "if(generated){var count=m.SendAll(request,destination,p.Token.Token);P.Check(count==3&&destination[3]==-9,\"Span complete commit and trailing sentinel\");results=destination.Take(3).ToArray();}else{results=" + a + "ReferenceAll.Run(sp,request,p.Token.Token);results.CopyTo(destination,0);}";
        return $$"""
            case "{{c.Name}}":{
                var request=new {{c.Request}}(1,shared);p.Original=request;{{(c.Async ? (c.Void ? "ValueTask" : "ValueTask<int[]>") + " task;" : "")}}
                try{ {{call}}
                    {{(c.Async ? "initial=task.IsCompletedSuccessfully?\"Success\":task.IsFaulted?\"Faulted\":task.IsCanceled?\"Canceled\":\"Pending\";" : "")}}
                    {{(c.Name == "ARString" ? """
                        var gate=p.Gate!;P.Check(initial=="Pending"&&p.Branch==2&&gate.Consumptions==0,"closed generic await boundary");
                        P.Check(!p.Resources.Values.Any(r=>r.Id=="H3"),"later branch not resolved while pending");
                        waiting=new{p.Branch,events=p.Events.ToArray(),trace=p.Trace.ToArray(),consumptions=gate.Consumptions};
                        var original=request;request=new(900,shared);shared.Version+=50;callerValue=request.Value;
                        if(generated){dispatches++;var reply=m.Send(new R.Ping(10),single.Token.Token);P.Check(reply.A==12,"Sync interleave");var values=new List<int>();dispatches++;await foreach(var item in m.StreamAsync(new S.Items(new S.SharedState())))values.Add(item);P.Check(values.SequenceEqual(new[]{2,4})&&stream.SourceDisposals==1,"Stream interleave");P.Check(p.Branch==2&&gate.Consumptions==0&&!task.IsCompleted,"interleave preserves pending closed generic state");interleave=new{sync=reply.A,values};}
                        gate.Complete();P.Check(original.Value==1,"operation request survives caller reference replacement");
                        """ : "")}}
                    {{(c.Async ? (c.Void ? "await task;" : "results=await task;") + "final=task.IsCompletedSuccessfully?\"Success\":task.IsCanceled?\"Canceled\":\"Faulted\";" : "")}}
                }catch(Exception e)when(ReferenceEquals(e,p.Failure)){error=p.Error(e);final="Faulted";}
                originalValue=(({{c.Request}})p.Original).Value;{{(c.Name == "ARString" ? "" : "callerValue=request.Value;")}}
                break;
            }
            """;
    }
    private static string AllGenericSource(bool gate)
    {
        var cases = gate ? AllGenericCases : AllGenericCases.Where(c => AllGenericRuntimeNames.Contains(c.Name)).ToArray();
        var common = SendAllCompositionSource(false); common = common[..common.IndexOf("namespace AllMix {", StringComparison.Ordinal)];
        var definitions = "namespace GenericCases{" + AllGenericRequestDefinition("Response", false) + AllGenericRequestDefinition("Command", true) + AllGenericRequestDefinition("LegacyResponse", false, true) + "}";
        var legacy = gate ? AllGenericModule(new("Legacy", false, false, "int", true)) : "";
        return common + AllGenericProbeSource + definitions + (gate ? AllClosedHandlerDefinitions() + AllClosedHookDefinitions() : "") + string.Join("", cases.Select(c => AllGenericModule(c) + (gate ? AllGenericFactoryGateExtras(c) + AllHandlerVisibilityExtras(c) + AllClosedHookVisibilityExtras(c) + AllGenericCompositionVisibilityExtras(c) : ""))) + legacy + (gate ? AllHandlerVisibilityExtras(new("Legacy", false, false, "int", true)) + AllClosedHookVisibilityExtras(new("Legacy", false, false, "int", true)) + AllGenericCompositionVisibilityExtras(new("Legacy", false, false, "int", true)) : "") + """
            namespace AllGeneric{
            using P=global::GenericCases.Probe;
            using S=global::Closed.AllMixStream;using R=global::Closed.AllMixSync;using A=global::Closed.AllMixAsync;
            using Zendiator.Generator.Tests;
            [ZendiatorComposition("all-generic")]public sealed class Many;
            public static class Bridge{
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{__CONFIG__});
                __CONSUMER__
            }
            __REJECTED__
            }
            """.Replace("__CONFIG__", (gate ? AllGenericFactoryGateConfiguration(cases) : AllGenericConfiguration(cases)) + "c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));c.Handlers<R.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<R.Handler,R.Factory>());c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());c.Pipeline<R.Ping>(DispatchFamily.SendSync,Stage.Hook<R.Hook>());")
            .Replace("__REJECTED__", gate ? AllGenericRejectedSource() : "")
            .Replace("__CONSUMER__", gate ? "" : """
                private static int dispatches;
                private static async Task<(string Snapshot,object? Interleave)> Observe(string profile,bool generated){
                    var p=new P{Wait=profile=="ARString",Fail=profile=="SRString"};var shared=new GenericCases.Shared();var single=new ManagedShapeProbe();var stream=new PublicValueRequestProbe();
                    try{
                        IServiceCollection defaults=new ServiceCollection();Register(defaults);
                        IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddSingleton(single);services.AddSingleton(stream);
                        foreach(var d in defaults.Where(d=>d.ServiceType.Namespace?.StartsWith("GenericCases.",StringComparison.Ordinal)==true)){P.Check(d.Lifetime==ServiceLifetime.Transient&&d.ImplementationType==d.ServiceType,"default callback/factory registration");services.AddScoped(d.ServiceType,d.ImplementationType!);}
                        Register(services);
                        await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});await using var scope=provider.CreateAsyncScope();var sp=scope.ServiceProvider;
                        var m=sp.GetRequiredService<global::AllGeneric.Generated.Many.IZendiator>();P.Check(ReferenceEquals(m,sp.GetRequiredService<global::AllGeneric.Generated.Many.IZendiator>()),"shared mediator identity");
                        int[]? results=null;var destination=new[]{701,702,703,-9};var initial="Success";var final="Success";var error="none";object? waiting=null;object? interleave=null;var callerValue=0;var originalValue=0;
                        dispatches++;switch(profile){__CASES__}
                        P.Check(originalValue==1,"caller original request remains unchanged");
                        P.Check(p.Handlers.SequenceEqual(p.Fail?new[]{2,1}:new[]{2,1,3})&&p.Branch==(p.Fail?2:3),"explicit handler order and failure stop");
                        if(p.Fail)P.Check(error=="sentinel"&&results==null&&destination.SequenceEqual(new[]{701,702,703,-9})&&!p.Resources.Values.Any(r=>r.Id=="H3"),"failure stops later branch and leaves Span unchanged");
                        else if(profile.StartsWith("SR",StringComparison.Ordinal)||profile.StartsWith("AR",StringComparison.Ordinal))P.Check(results!.SequenceEqual(new[]{14,13,15}),"ordered response results");
                        P.Check(shared.Version==(p.Wait?68:p.Fail?7:18),"shared reference mutations visible between branches");
                        if(p.Wait)P.Check(callerValue==900&&p.Gate!.Consumptions==1,"pending task consumed exactly once with local caller replacement");
                        P.Check(p.FactoryEnds.Count==0,"factory services live until scope disposal");
                        foreach(var resource in p.Resources)P.Check(resource.Value.Owned==p.Released.Contains(resource.Key),"only operation-owned resources cleaned up");
                        var cleanup=p.Trace.Where(e=>e.StartsWith("cleanup:",StringComparison.Ordinal)).ToArray();
                        P.Check(cleanup.SequenceEqual(p.Fail?new[]{"cleanup:H2","cleanup:K0","cleanup:K0"}:new[]{"cleanup:H2","cleanup:K0","cleanup:K0","cleanup:K0"}),"reverse acquisition cleanup and borrowed receiver exclusion");
                        var beforeScope=p.Released.Select(r=>p.Resources[r].Id).ToArray();await scope.DisposeAsync();
                        P.Check(p.FactoryEnds.SequenceEqual(p.Factories.AsEnumerable().Reverse()),"DI-owned factories dispose in reverse order");
                        foreach(var resource in p.Resources)P.Check((resource.Value.Owned||resource.Value.Direct)==p.Released.Contains(resource.Key),"DI scope owns direct services; external owner retains borrowed sessions");
                        var snapshot=JsonSerializer.Serialize(new{profile,results,destination,initial,final,error,p.Branch,p.Handlers,p.Events,p.Trace,p.Factories,p.FactoryEnds,created=p.Resources.Values.Select(r=>new{r.Id,r.Owned,r.Direct}),released=p.Released.Select(r=>p.Resources[r].Id),beforeScope,shared=shared.Version,callerValue,originalValue,waiting,consumptions=p.Gate?.Consumptions??0});
                        foreach(var resource in p.Resources.Keys.Where(r=>!p.Released.Contains(r)).ToArray()){if(profile[0]=='A')await ((IAsyncDisposable)resource).DisposeAsync();else ((IDisposable)resource).Dispose();}
                        P.Check(p.Released.Count==p.Resources.Count,"external borrowed owner cleanup");return(snapshot,interleave);
                    }finally{p.Token.Dispose();single.Token.Dispose();}
                }
                public static async Task<string> Run(){
                    dispatches=0;var rows=new List<object>();object? interleave=null;
                    foreach(var profile in new[]{"SRInt","SRString","SVInt","ARString","AVInt","AVString"}){
                        var g=await Observe(profile,true);var r=await Observe(profile,false);P.Check(g.Snapshot==r.Snapshot,"generic generated/reference mismatch "+profile+"\n"+g.Snapshot+"\n"+r.Snapshot);
                        rows.Add(new{profile,generated=JsonDocument.Parse(g.Snapshot).RootElement.Clone(),reference=JsonDocument.Parse(r.Snapshot).RootElement.Clone()});if(g.Interleave!=null)interleave=g.Interleave;
                    }
                    P.Check(dispatches==14,"six pairs and two interleaves");return JsonSerializer.Serialize(new{generatorInputs=1,pairedScenarios=6,pairedDispatches=12,interleaveDispatches=2,dispatches,rows,interleave});
                }
                """.Replace("__CASES__", string.Join("", cases.Select(AllGenericConsumerCase))));
    }
    private static object AssertAllGenericContract(Compilation output, GeneratorDriver driver, bool gate, AllGenericCase[]? selected = null)
    {
        var cases = selected ?? (gate ? AllGenericCases : AllGenericCases.Where(c => AllGenericRuntimeNames.Contains(c.Name)).ToArray());
        var api = output.GetTypeByMetadataName("AllGeneric.Generated.Many.IZendiator"); Assert.NotNull(api);
        var methods = api.GetMembers().OfType<IMethodSymbol>().Where(m => m.Name is "SendAll" or "SendAllAsync").ToArray();
        Assert.Equal(cases.Sum(c => !c.Async && !c.Void ? 2 : 1), methods.Length);
        foreach (var c in cases)
        {
            var request = c.Request.Replace("global::", "", StringComparison.Ordinal); var name = c.Async ? "SendAllAsync" : "SendAll";
            var found = methods.Where(m => m.Name == name && m.Parameters[0].Type.ToDisplayString() == request).ToArray();
            Assert.Equal(!c.Async && !c.Void ? 2 : 1, found.Length);
            Assert.All(found, m => { var t = Assert.IsAssignableFrom<INamedTypeSymbol>(m.Parameters[0].Type); Assert.True(t.IsGenericType); Assert.True(t.IsReferenceType); Assert.False(t.IsUnboundGenericType); });
        }
        var registrar = Assert.Single(output.SyntaxTrees.SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()), t => t.Identifier.ValueText == "ZendiatorGeneratedRegistrar" && t.Ancestors().OfType<NamespaceDeclarationSyntax>().Any(n => n.Name.ToString() == "AllGeneric.Generated.Many"));
        var calls = registrar.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(i => i.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "expected" }, Name: GenericNameSyntax }).ToArray();
        string Request(InvocationExpressionSyntax i) => ((GenericNameSyntax)((MemberAccessExpressionSyntax)i.Expression).Name).TypeArgumentList.Arguments[0].ToString();
        var handlerRequests = calls.Where(i => ((MemberAccessExpressionSyntax)i.Expression).Name.Identifier.ValueText == "Handlers").Select(Request).ToArray();
        var pipelineRequests = calls.Where(i => ((MemberAccessExpressionSyntax)i.Expression).Name.Identifier.ValueText == "Pipeline").Select(Request).ToArray();
        Assert.Equal(cases.Select(c => c.Request).Concat(["global::Closed.AllMixStream.Items", "global::Closed.AllMixSync.Ping"]), handlerRequests);
        Assert.Equal(cases.Reverse().Select(c => c.Request).Concat(["global::Closed.AllMixStream.Items", "global::Closed.AllMixSync.Ping"]), pipelineRequests);
        return new { publicAllMethods = methods.Select(m => m.ToDisplayString()), handlerRequests, pipelineRequests };
    }
    private static string? AllGenericFolder(string name)
    { var e = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(e)) return null; var p = Path.Combine(e, "all-generic-" + name); Directory.CreateDirectory(p); return p; }
    [Fact]
    public void AllGeneric_gate_preserves_closed_type_identity_and_symbol_based_receiver_factory_boundaries()
    {
        var source = AllGenericSource(true); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllGenericGate_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); var folder = AllGenericFolder("gate"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Equal(67, diagnostics.Length);
        var blocking = Assert.Single(diagnostics, d => d.Id == "ZEN0030"); Assert.Contains("SRInt.MixedFull", blocking.GetMessage());
        var wrongRequests = diagnostics.Where(d => d.Id == "ZEN0035").ToArray(); Assert.Equal(2, wrongRequests.Length);
        var wrongFactories = diagnostics.Where(d => d.Id == "ZEN0026" && d.GetMessage().StartsWith("Factory ", StringComparison.Ordinal)).ToArray(); Assert.Equal(4, wrongFactories.Length);
        var wrongHooks = diagnostics.Where(d => d.Id == "ZEN0026" && !d.GetMessage().StartsWith("Factory ", StringComparison.Ordinal)).ToArray(); Assert.Equal(2, wrongHooks.Length);
        Assert.All(diagnostics.Where(d => !ReferenceEquals(d, blocking) && d.Id is not ("ZEN0035" or "ZEN0026")), d => Assert.Equal("ZEN0037", d.Id));
        var compiler = output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).ToArray();
        Assert.Equal(4, compiler.Length); Assert.All(compiler, d => { Assert.Equal("CS0452", d.Id); Assert.Equal(source, d.Location.SourceTree!.GetText().ToString()); });
        var contract = AssertAllGenericContract(output, driver, true);
        var files = driver.GetRunResult().Results.Single().GeneratedSources;
        foreach (var c in new[] { AllGenericCases[0], AllGenericCases[5] })
        {
            foreach (var newlyAccepted in new[] { "wrapper", "handler", "handlerWrapper", "hook", "hookWrapper", "factory", "hookFactory", "genericWrapper" })
            {
                var accepted = "AllGeneric.Generated.Reject" + c.Name + newlyAccepted;
                Assert.NotNull(output.GetTypeByMetadataName(accepted + ".IZendiator")); Assert.NotNull(output.GetTypeByMetadataName(accepted + ".ZendiatorGeneratedRegistrar"));
                Assert.Contains(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains(accepted + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
            }
            foreach (var mutation in new[] { "before", "partialWrapper", "mixedWrapper", "onlyWrapper", "multipleWrappers" })
            {
                var ns = "AllGeneric.Generated.Reject" + c.Name + mutation;
                Assert.Null(output.GetTypeByMetadataName(ns + ".IZendiator")); Assert.Null(output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar"));
                Assert.DoesNotContain(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains(ns + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
                var start = source.IndexOf("public static class Reject" + c.Name + mutation + "Bridge", StringComparison.Ordinal); var end = source.IndexOf("});}", start, StringComparison.Ordinal);
                Assert.Single(diagnostics, d => d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end);
            }
        }
        Assert.Null(output.GetTypeByMetadataName("AllGeneric.Generated.RejectWhole.IZendiator")); Assert.Null(output.GetTypeByMetadataName("AllGeneric.Generated.RejectWhole.ZendiatorGeneratedRegistrar"));
        Assert.DoesNotContain(files, f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains("AllGeneric.Generated.RejectWhole.ZendiatorGeneratedRegistrar", StringComparison.Ordinal));
        var wholeStart = source.IndexOf("public static class RejectWholeBridge", StringComparison.Ordinal); var wholeEnd = source.IndexOf("});}", wholeStart, StringComparison.Ordinal);
        var wholeDiagnostics = diagnostics.Where(d => d.Location.SourceSpan.Start >= wholeStart && d.Location.SourceSpan.Start < wholeEnd).ToArray(); Assert.Equal(8, wholeDiagnostics.Length);
        foreach (var c in AllGenericCases)
        {
            var start = source.IndexOf("c.Pipeline<" + c.Request + ">(", wholeStart, StringComparison.Ordinal); var end = source.IndexOf(';', start);
            Assert.Single(wholeDiagnostics, d => d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end);
        }
        foreach (var mutation in new[] { "handler", "hook", "factory", "hookFactory" }) Assert.NotNull(output.GetTypeByMetadataName("AllGeneric.Generated.Legacy" + mutation + ".IZendiator"));
        foreach (var c in new[] { AllGenericCases[0], AllGenericCases[5] })
            foreach (var wrapped in new[] { false, true })
                foreach (var name in new[] { "Internal", "Containing", "Argument", "PublicNested", "OldInternalPlain", "Constraint", "WrongRequest" }.Where(n => !wrapped || n is not ("Constraint" or "WrongRequest")))
                {
                    var marker = "Visibility" + c.Name + name + (wrapped ? "Wrapped" : ""); var ns = "AllGeneric.Generated." + marker; var accepted = name is "PublicNested" or "OldInternalPlain";
                    Assert.Equal(accepted, output.GetTypeByMetadataName(ns + ".IZendiator") != null); Assert.Equal(accepted, output.GetTypeByMetadataName(ns + ".ZendiatorGeneratedRegistrar") != null);
                    Assert.Equal(accepted, files.Any(f => f.HintName.EndsWith("Interceptors.g.cs", StringComparison.Ordinal) && f.SourceText.ToString().Contains(ns + ".ZendiatorGeneratedRegistrar", StringComparison.Ordinal)));
                    var start = source.IndexOf("public static class " + marker + "Bridge", StringComparison.Ordinal); var end = source.IndexOf("});}", start, StringComparison.Ordinal);
                    Assert.Equal(accepted || name == "Constraint" ? 0 : 1, diagnostics.Count(d => d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end));
                    if (name == "Constraint") Assert.Single(compiler, d => d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end);
                    if (name == "WrongRequest") Assert.Single(wrongRequests, d => d.Location.SourceSpan.Start >= start && d.Location.SourceSpan.Start < end);
                }
        Assert.NotNull(output.GetTypeByMetadataName("AllGeneric.Generated.LegacyInternalGeneric.IZendiator"));
        AssertAllClosedHookGate(source, output, files.Select(f => (f.HintName, f.SourceText.ToString())).ToArray(), diagnostics, compiler);
        AssertAllGenericCompositionGate(source, output, files.Select(f => (f.HintName, f.SourceText.ToString())).ToArray(), diagnostics);
        var catalog = Assert.IsType<CompositionSelectionCatalog>(Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s => s.Outputs)).Value);
        var pipelines = catalog.Compositions.SelectMany(c => c.Pipelines).ToArray();
        foreach (var p in pipelines)
            foreach (var s in p.Stages.Where(s => s.Type.Contains(".MixedFull", StringComparison.Ordinal)))
            {
                if (p.Family == BoundDispatchFamily.SendAllSync) Assert.Null(s.Capabilities); else Assert.NotNull(s.Capabilities);
                Assert.Null(s.SyncEmission); Assert.Null(s.AsyncEmission);
            }
        foreach (var c in AllGenericCases) Assert.Contains(pipelines, p => p.Stages.Any(s => s.Name == ClosedHookName(c) && s.Sessions.All(f => f.FactoryIsGeneric && f.FactoryIsPublic)) && p.Handlers.Where(h => h.Sessions.Count != 0).All(h => h.Sessions.All(f => f.FactoryIsGeneric && f.FactoryIsPublic)) && !p.Stages.Any(s => s.Kind == FusionStageKind.Wrapper) && p.Handlers.Count == 3 && p.Handlers.All(h => h.ReceiverIsGeneric && h.ReceiverIsPublic) && p.Stages.Any(s => s.Name == ClosedHookName(c) && s.ReceiverIsGeneric && s.ReceiverIsPublic && (c.Async ? s.AsyncEmission != null : s.SyncEmission != null)));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Stages.Any(s => s.ReceiverIsGeneric && !s.ReceiverIsPublic));
        Assert.Contains(pipelines, r => !r.RequestIsGeneric && r.Stages.Any(s => s.ReceiverIsGeneric && !s.ReceiverIsPublic));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Stages.Any(s => !s.ReceiverIsGeneric && !s.ReceiverIsPublic));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Handlers.Any(h => h.ReceiverIsGeneric && !h.ReceiverIsPublic));
        Assert.Contains(pipelines, r => !r.RequestIsGeneric && r.Handlers.Any(h => h.ReceiverIsGeneric && !h.ReceiverIsPublic));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Handlers.Any(h => !h.ReceiverIsGeneric && !h.ReceiverIsPublic));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Handlers.Any(h => h.ReceiverIsGeneric));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Stages.Any(s => s.ReceiverIsGeneric));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Handlers.Any(h => h.Sessions.Any(f => f.FactoryIsGeneric)));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Stages.Any(s => s.Kind == FusionStageKind.Wrapper) && r.Handlers.Any(h => h.ReceiverIsGeneric && h.ReceiverIsPublic) && r.Handlers.Any(h => h.Sessions.Any(f => f.FactoryIsGeneric)));
        Assert.Contains(pipelines, r => r.RequestIsGeneric && r.Stages.Any(s => s.Sessions.Any(f => f.FactoryIsGeneric)));
        Assert.Contains(pipelines, r => !r.RequestIsGeneric && r.Handlers.Any(h => h.ReceiverIsGeneric));
        Assert.Contains(pipelines, r => !r.RequestIsGeneric && r.Stages.Any(s => s.ReceiverIsGeneric));
        Assert.Contains(pipelines, r => !r.RequestIsGeneric && r.Handlers.Any(h => h.Sessions.Any(f => f.FactoryIsGeneric)));
        Assert.Contains(pipelines, r => !r.RequestIsGeneric && r.Stages.Any(s => s.Sessions.Any(f => f.FactoryIsGeneric)));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "gate-result.json"), JsonSerializer.Serialize(new { generatorInputs = 1, dispatches = 0, genericHandlerRoutes = 8, genericHookRoutes = 8, newlyAcceptedOldHandlerCases = 2, acceptedOldWrapperCases = 2, newlyAcceptedHandlerWrapperCases = 2, newlyAcceptedHookCases = 2, publicNestedCases = 4, oldInternalPlainCases = 4, legacyInternalGenericCases = 1, legacyGenericServiceRoutes = 4, publicRejectedCases = 12, wrapperPublicRejectedCases = 6, hookPublicRejectedCases = 6, newlyAcceptedHookWrapperCases = 2, newlyAcceptedGenericWrapperCases = 2, newlyAcceptedGenericHandlerFactoryCases = 2, newlyAcceptedGenericHookFactoryCases = 4, factoryPublicRejectedCases = 12, genericWrapperPublicRejectedCases = 6, hookMultipleRejectedCases = 2, acceptedExistingTwoHookWrappers = 2, factoryBindingMismatchDiagnostics = 4, rejectedPipelineDiagnostics = 67, incompleteExecutionDiagnostics = 58, synchronousBlockingDiagnostic = 1, wrongRequestDiagnostics = 2, wrongHookDiagnostics = 2, compilerConstraintErrors = 4, publicProofIncludesContainersAndArguments = true, wholeCompositionPartialHookRejectedRoutes = wholeDiagnostics.Length, familySpecificFullHookProof = true, genericFactoryRoutes = 8, contract }));
    }
    [Fact]
    public async Task AllGeneric_four_profiles_preserve_reference_locality_pending_ownership_and_failure_Span_commit()
    {
        var source = AllGenericSource(false); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllGenericRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); var folder = AllGenericFolder("runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); var contract = AssertAllGenericContract(output, driver, false);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) { File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(folder, "contract.json"), JsonSerializer.Serialize(contract)); }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = await assembly.GetType("AllGeneric.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result); using var json = JsonDocument.Parse(result); Assert.Equal(14, json.RootElement.GetProperty("dispatches").GetInt32());
    }
}
