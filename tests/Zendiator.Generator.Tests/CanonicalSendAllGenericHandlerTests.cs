using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static AllGenericCase[] AllClosedHandlerCases => [AllGenericCases[0], AllGenericCases[2], AllGenericCases[5], AllGenericCases[7]];
    private static string ClosedHandlerName(AllGenericCase c, int id) => "global::GenericCases.Handlers." + (c.Async ? "Async" : "Sync") + (c.Void ? "Command" : "Response") + "<" + c.Argument + ",global::GenericCases.Tag" + id + ">";
    private static string AllClosedHandlerDefinitions(bool wrapper = false)
    {
        var text = "namespace GenericCases{public interface IBranchTag{int Id{get;}}" + string.Join("", new[] { 1, 2, 3 }.Select(id => "public sealed class Tag" + id + ":IBranchTag{public int Id=>" + id + ";}")) + "internal sealed class HiddenTag{}}namespace GenericCases.Handlers{";
        foreach (var c in AllGenericCases.Where(c => c.Argument == "int"))
        {
            var name = (c.Async ? "Async" : "Sync") + (c.Void ? "Command" : "Response");
            var request = "global::GenericCases." + (c.Void ? "Command" : "Response") + "<T>";
            var ret = c.Async ? c.Void ? "ValueTask" : "ValueTask<int>" : c.Void ? "void" : "int";
            text += $$"""
                [ZendiatorHandler]public class {{name}}<T,B>:{{(c.Async ? "IRequestHandler" : "ISyncRequestHandler")}}<{{request}}{{(c.Void ? "" : ",int")}}>,{{(c.Async ? "IAsyncDisposable" : "IDisposable")}} where B:global::GenericCases.IBranchTag,new(){
                    private readonly global::GenericCases.Probe p;private readonly int id;
                    public {{name}}(global::GenericCases.Probe p){this.p=p;id=new B().Id;p.Trace.Add("handler-type:"+GetType());p.Created(this,"H"+id,id==2,id==3);}
                    public {{ret}} {{(c.Async ? "HandleAsync" : "Handle")}}({{request}} r,CancellationToken t){
                        r.Mark(p,"H"+id+".body",t);p.Handlers.Add(id);if({{(wrapper ? "p.FailBody(id)" : "p.Fail&&id==1")}})throw p.Failure;
                        var value=r.Value+id;r.Value+=100;r.Shared.Version+=5;r.Mark(p,"H"+id+".local",t);
                        {{(c.Async ? "return p." + (c.Void ? "BodyVoid" : "BodyResponse") + "(value,id);" : c.Void ? "" : "return value;")}}
                    }
                    {{(c.Async ? "public ValueTask DisposeAsync(){p.Cleanup(this);return default;}" : "public void Dispose()=>p.Cleanup(this);")}}
                }
                """;
        }
        return text + "}";
    }
    private static string AllHandlerVisibilityExtras(AllGenericCase c) => $$"""
        namespace GenericCases.{{c.Name}}{
        [ZendiatorHandler]internal sealed class InternalH<T>:H3{public InternalH(Probe p):base(p){} }
        [ZendiatorHandler]internal sealed class InternalPlain:H3{public InternalPlain(Probe p):base(p){} }
        internal static class InternalHost{[ZendiatorHandler]public sealed class Nested<T>:H3{public Nested(Probe p):base(p){} } }
        public static class PublicHost{[ZendiatorHandler]public sealed class Nested<T>:H3{public Nested(Probe p):base(p){} } }
        [ZendiatorHandler]public sealed class ConstrainedH<T>:H3 where T:class{public ConstrainedH(Probe p):base(p){} }
        }
        """;
    private static string AllClosedHandlerExtras(AllGenericCase c, bool wrapper = false, bool genericHook = false)
    {
        var text = "namespace GenericCases." + c.Name + "{using Ping=" + c.Request + ";" + string.Join("", new[] { 1, 2, 3 }.Select(id => "using G" + id + "=" + ClosedHandlerName(c, id) + ";"));
        if (genericHook) text += "using GHook=" + ClosedHookName(c) + ";" + AllClosedHookFactory(c);
        foreach (var id in new[] { 2, 1, 3 })
        {
            var mode = AllGenericHandlerMode(c, id); var af = mode.StartsWith("async-", StringComparison.Ordinal); var owned = mode.EndsWith("owned", StringComparison.Ordinal);
            if (mode != "direct") text += $$"""
                public sealed class GF{{id}}:I{{(af ? "Async" : "")}}{{(owned ? "Owned" : "Borrowed")}}SessionFactory<Ping,G{{id}}>,IDisposable{
                    private readonly Probe p;public GF{{id}}(Probe p){this.p=p;p.Factories.Add("GF{{id}}");}
                    public {{(af ? "ValueTask<G" + id + ">" : "G" + id)}} {{(af ? "CreateAsync" : "Create")}}(Ping r,CancellationToken t){r.Mark(p,"GF{{id}}.acquire",t);var value=new G{{id}}(p);return {{(af ? "new(value)" : "value")}};}
                    public void Dispose()=>p.FactoryEnds.Add("GF{{id}}");
                }
                """;
            var completion = c.Async ? "a" : "s";
            var reference = CanonicalCapabilityReference.Source(c.Async, c.Void, completion, completion, completion, completion, false, wrapper ? "direct" : AllGenericHookMode(c), mode, wrapper);
            reference = Regex.Replace(reference, @"\bOracle\b", "GenericReference" + id); reference = Regex.Replace(reference, @"\bHandler\b", "G" + id); reference = Regex.Replace(reference, @"\bHFactory\b", "GF" + id);
            if (genericHook) { reference = Regex.Replace(reference, @"\bHook0\b", "GHook"); reference = Regex.Replace(reference, @"\bKFactory\b", "GKFactory"); }
            text += reference.Replace("namespace App {", "", StringComparison.Ordinal)[..^1];
        }
        var ret = c.Async ? c.Void ? "ValueTask" : "ValueTask<int[]>" : c.Void ? "void" : "int[]";
        var calls = string.Join("", new[] { 2, 1, 3 }.Select((id, i) => (c.Void ? "" : "results[" + i + "]=") + (c.Async ? "await " : "") + "GenericReference" + id + ".Send(p,r,t)" + (c.Async ? ".ConfigureAwait(false)" : "") + ";"));
        return text + "public static class GenericReferenceAll{public static " + (c.Async ? "async " : "") + ret + " Run(IServiceProvider p,Ping r,CancellationToken t){ArgumentNullException.ThrowIfNull(r);t.ThrowIfCancellationRequested();" + (c.Void ? "" : "var results=new int[3];") + calls + (c.Void ? "" : "return results;") + "}}}";
    }
    private static string AllClosedHandlerConfiguration(IEnumerable<AllGenericCase> cases, bool wrapper = false, bool genericHook = false)
    {
        var handlers = new List<string>(); var pipelines = new List<string>();
        foreach (var c in cases)
        {
            var a = "global::GenericCases." + c.Name + ".";
            var entries = new[] { 2, 1, 3 }.Select(id => id == 3 ? "typeof(" + ClosedHandlerName(c, id) + ")" : "HandlerEntry.Session<" + ClosedHandlerName(c, id) + "," + a + "GF" + id + ">()");
            handlers.Add($"c.Handlers<{c.Request}>(DispatchFamily.{c.Family},{string.Join(",", entries)});");
            var hook = genericHook ? ClosedHookName(c) : a + "Hook0"; var factory = a + (genericHook ? "GKFactory" : "KFactory");
            pipelines.Add($"c.Pipeline<{c.Request}>(DispatchFamily.{c.Family},Stage.Hook<{hook}{(wrapper ? "" : "," + factory)}>(){(wrapper ? ",Stage.Wrapper<" + a + "W>(),Stage.Hook<" + a + "Hook1>()" : "")});");
        }
        return string.Join("", handlers) + string.Join("", pipelines.AsEnumerable().Reverse());
    }
    private static string AllHandlerVisibilityCases()
    {
        var text = "";
        foreach (var c in new[] { AllGenericCases[0], AllGenericCases[5] })
        {
            foreach (var wrapped in new[] { false, true })
                foreach (var (name, type) in new[] { ("Internal", "InternalH<string>"), ("Containing", "InternalHost.Nested<string>"), ("Argument", "GenericH3<global::GenericCases.HiddenTag>"), ("PublicNested", "PublicHost.Nested<string>"), ("OldInternalPlain", "InternalPlain"), ("Constraint", "ConstrainedH<int>") }.Where(p => !wrapped || p.Item1 != "Constraint"))
                {
                    var marker = "Visibility" + c.Name + name + (wrapped ? "Wrapped" : ""); var a = "global::GenericCases." + c.Name + ".";
                    var config = AllGenericConfiguration([c], wrapped ? "wrapper" : "").Replace("typeof(" + a + "H3)", "typeof(" + a + type + ")", StringComparison.Ordinal);
                    text += "[ZendiatorComposition(\"" + marker + "\")]public sealed class " + marker + ";public static class " + marker + "Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<" + marker + ">(c=>{" + config + "});}";
                }
            var wrong = c with { Argument = c.Argument == "int" ? "string" : "int" }; var markerWrong = "Visibility" + c.Name + "WrongRequest";
            var wrongConfig = AllGenericConfiguration([c]).Replace("typeof(global::GenericCases." + c.Name + ".H3)", "typeof(" + ClosedHandlerName(wrong, 3) + ")", StringComparison.Ordinal);
            text += "[ZendiatorComposition(\"" + markerWrong + "\")]public sealed class " + markerWrong + ";public static class " + markerWrong + "Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<" + markerWrong + ">(c=>{" + wrongConfig + "});}";
        }
        var legacy = new AllGenericCase("Legacy", false, false, "int", true);
        text += "[ZendiatorComposition(\"legacy-internal-generic\")]public sealed class LegacyInternalGeneric;public static class LegacyInternalGenericBridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<LegacyInternalGeneric>(c=>{" + AllGenericConfiguration([legacy]).Replace("typeof(global::GenericCases.Legacy.H3)", "typeof(global::GenericCases.Legacy.InternalH<string>)", StringComparison.Ordinal) + "});}";
        return text;
    }
    private static string AllClosedHandlerSource(bool genericHook = false)
    {
        var common = SendAllCompositionSource(false); common = common[..common.IndexOf("namespace AllMix {", StringComparison.Ordinal)];
        var definitions = "namespace GenericCases{" + AllGenericRequestDefinition("Response", false) + AllGenericRequestDefinition("Command", true) + "}";
        var cases = string.Join("", AllClosedHandlerCases.Select(c => (genericHook ? AllClosedHandlerWrapperConsumerCase(c) : AllGenericConsumerCase(c)).Replace(".ReferenceAll.Run", ".GenericReferenceAll.Run", StringComparison.Ordinal)
            .Replace("break;", "expectedTypes=new[]{" + string.Join(",", (c.Name == "AVString" ? new[] { 2, 1 } : new[] { 2, 1, 3 }).Select(id => "typeof(" + ClosedHandlerName(c, id) + ").ToString()")) + "};break;", StringComparison.Ordinal)));
        return common + AllGenericProbeSource + definitions + AllClosedHandlerDefinitions() + (genericHook ? AllClosedHookDefinitions() : "") + string.Join("", AllClosedHandlerCases.Select(c => AllGenericModule(c) + AllClosedHandlerExtras(c, genericHook: genericHook))) + """
            namespace AllGeneric{
            using P=global::GenericCases.Probe;
            using S=global::Closed.AllMixStream;using R=global::Closed.AllMixSync;
            using Zendiator.Generator.Tests;
            [ZendiatorComposition("all-closed-handlers")]public sealed class Many;
            public static class Bridge{
                public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Many>(c=>{__CONFIG__});
                private static int dispatches;
                private static async Task<(string Snapshot,object? Interleave)> Observe(string profile,bool generated){
                    var p=new P{Wait=profile=="ARString",Fail=profile=="AVString"};var shared=new GenericCases.Shared();var single=new ManagedShapeProbe();var stream=new PublicValueRequestProbe();
                    try{
                        IServiceCollection services=new ServiceCollection();services.AddSingleton(p);services.AddSingleton(single);services.AddSingleton(stream);Register(services);
                        await using var provider=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});await using var scope=provider.CreateAsyncScope();var sp=scope.ServiceProvider;
                        var m=sp.GetRequiredService<global::AllGeneric.Generated.Many.IZendiator>();P.Check(ReferenceEquals(m,sp.GetRequiredService<global::AllGeneric.Generated.Many.Zendiator>()),"shared mediator identity");
                        int[]? results=null;var destination=new[]{701,702,703,-9};var initial="Success";var final="Success";var error="none";object? waiting=null;object? interleave=null;var callerValue=0;var originalValue=0;string[] expectedTypes=Array.Empty<string>();
                        dispatches++;switch(profile){__CASES__}
                        P.Check(originalValue==1,"caller original request remains unchanged");P.Check(p.Branch==(p.Fail?2:3)&&p.Handlers.SequenceEqual(p.Fail?new[]{2,1}:new[]{2,1,3}),"ordered generic handler branches and failure stop");
                        var actualTypes=p.Trace.Where(e=>e.StartsWith("handler-type:",StringComparison.Ordinal)).Select(e=>e.Substring("handler-type:".Length)).ToArray();
                        P.Check(actualTypes.SequenceEqual(expectedTypes)&&actualTypes.Distinct().Count()==expectedTypes.Length,"actual request type argument and branch tag DI types remain distinct");
                        __HOOK_TYPE_CHECK__
                        if(profile=="SRInt")P.Check(results!.SequenceEqual(new[]{14,13,15})&&destination.SequenceEqual(new[]{14,13,15,-9}),"generic handler Span complete ordered commit");
                        if(profile=="ARString")P.Check(results!.SequenceEqual(new[]{14,13,15})&&callerValue==900&&p.Gate!.Consumptions==1,"generic handler pending task consumes once after caller replacement");
                        if(p.Fail)P.Check(error=="sentinel"&&final=="Faulted"&&results==null&&!p.Resources.Values.Any(r=>r.Id=="H3"),"failure stops later generic DI handler");
                        P.Check(shared.Version==(p.Wait?68:p.Fail?7:18),"shared reference changes and branch-local replacement");
                        P.Check(p.FactoryEnds.Count==0,"DI factory services remain until scope");
                        foreach(var resource in p.Resources)P.Check(resource.Value.Owned==p.Released.Contains(resource.Key),"only owned operation resources cleaned up");
                        P.Check(p.Trace.Where(e=>e.StartsWith("cleanup:",StringComparison.Ordinal)).SequenceEqual(p.Fail?new[]{"cleanup:H2","cleanup:K0","cleanup:K0"}:new[]{"cleanup:H2","cleanup:K0","cleanup:K0","cleanup:K0"}),"generic receiver cleanup order");
                        var beforeScope=p.Released.Select(r=>p.Resources[r].Id).ToArray();await scope.DisposeAsync();
                        P.Check(p.FactoryEnds.SequenceEqual(p.Factories.AsEnumerable().Reverse()),"DI factory reverse disposal");
                        foreach(var resource in p.Resources)P.Check((resource.Value.Owned||resource.Value.Direct)==p.Released.Contains(resource.Key),"DI owns direct generic handler; borrowed sessions remain external");
                        var snapshot=JsonSerializer.Serialize(new{profile,results,destination,initial,final,error,p.Branch,p.Handlers,actualTypes,expectedTypes,__HOOK_TYPE_FIELDS__p.Events,p.Trace,p.Factories,p.FactoryEnds,created=p.Resources.Values.Select(r=>new{r.Id,r.Owned,r.Direct}),released=p.Released.Select(r=>p.Resources[r].Id),beforeScope,shared=shared.Version,callerValue,originalValue,waiting,consumptions=p.Gate?.Consumptions??0});
                        foreach(var resource in p.Resources.Keys.Where(r=>!p.Released.Contains(r)).ToArray()){if(profile[0]=='A')await ((IAsyncDisposable)resource).DisposeAsync();else ((IDisposable)resource).Dispose();}
                        P.Check(p.Released.Count==p.Resources.Count,"external borrowed generic receiver cleanup");return(snapshot,interleave);
                    }finally{p.Token.Dispose();single.Token.Dispose();}
                }
                public static async Task<string> Run(){
                    dispatches=0;var rows=new List<object>();object? interleave=null;
                    foreach(var profile in new[]{"SRInt","SVInt","ARString","AVString"}){
                        var g=await Observe(profile,true);var r=await Observe(profile,false);P.Check(g.Snapshot==r.Snapshot,"closed generic handler generated/reference mismatch "+profile+"\n"+g.Snapshot+"\n"+r.Snapshot);
                        rows.Add(new{profile,generated=JsonDocument.Parse(g.Snapshot).RootElement.Clone(),reference=JsonDocument.Parse(r.Snapshot).RootElement.Clone()});if(g.Interleave!=null)interleave=g.Interleave;
                    }
                    P.Check(dispatches==__DISPATCHES__,"four pairs and configured interleaves");return JsonSerializer.Serialize(new{generatorInputs=1,pairedScenarios=4,pairedDispatches=8,interleaveDispatches=__INTERLEAVES__,dispatches,rows,interleave});
                }
            }}
            """.Replace("__CONFIG__", AllClosedHandlerConfiguration(AllClosedHandlerCases, genericHook: genericHook) + "c.Handlers<S.Items>(DispatchFamily.Stream,typeof(S.H));c.Handlers<R.Ping>(DispatchFamily.SendSync,HandlerEntry.Session<R.Handler,R.Factory>());c.Pipeline<S.Items>(DispatchFamily.Stream,Stage.Hook<S.F1>(),Stage.Hook<S.M>(),Stage.Hook<S.F2>());c.Pipeline<R.Ping>(DispatchFamily.SendSync,Stage.Hook<R.Hook>());")
            .Replace("__CASES__", cases).Replace("__HOOK_TYPE_CHECK__", genericHook ? AllClosedHookTypeCheck() : "", StringComparison.Ordinal).Replace("__HOOK_TYPE_FIELDS__", genericHook ? "actualHookTypes,expectedHookType," : "", StringComparison.Ordinal)
            .Replace("__DISPATCHES__", genericHook ? "8" : "10", StringComparison.Ordinal).Replace("__INTERLEAVES__", genericHook ? "0" : "2", StringComparison.Ordinal);
    }
    [Fact]
    public async Task AllClosedHandler_four_profiles_preserve_actual_type_arguments_DI_branch_tags_pending_and_ownership()
    {
        var source = AllClosedHandlerSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AllClosedHandlerRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics); var folder = AllGenericFolder("handler-runtime"); SaveSendAllSync(folder, source, driver, output, diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); var contract = AssertAllGenericContract(output, driver, false, AllClosedHandlerCases);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); if (folder != null) { File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(folder, "contract.json"), JsonSerializer.Serialize(contract)); }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var result = await assembly.GetType("AllGeneric.Bridge")!.GetMethod("Run")!.CreateDelegate<Func<Task<string>>>()().WaitAsync(TimeSpan.FromSeconds(20));
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), result); using var json = JsonDocument.Parse(result); Assert.Equal(10, json.RootElement.GetProperty("dispatches").GetInt32());
    }
}
