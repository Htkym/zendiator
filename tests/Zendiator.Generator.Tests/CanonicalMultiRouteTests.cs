using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record MultiFixture(Assembly Assembly,Type Mediator,MethodInfo Register,ManagedShapeFixture A,ManagedShapeFixture B,string Body);
    private static readonly ConcurrentDictionary<string,Lazy<MultiFixture>> MultiFixtures=new();
    private static string MultiArtifactRoot=>Path.GetDirectoryName(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))!;
    private static void MultiEvidence(string folder,string name,object value)
    {if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR")))return;var p=Path.Combine(MultiArtifactRoot,folder);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,name+".json"),JsonSerializer.Serialize(value));}
    private static (bool Async,bool Struct,string Result,bool Wrapper) MultiShape(string profile,bool second)=>profile switch{
        "mixed"=>second?(true,false,"array",true):(false,true,"large",false),
        "same-family"=>second?(true,false,"large",false):(true,true,"nullable",false),
        _=>second?(true,true,"large",true):(false,false,"array",true)};
    private static string MultiRouteSource(string profile,bool second)
    {
        var k=MultiShape(profile,second);var source=ManagedShapeSource(k.Async,k.Struct,k.Result,k.Wrapper);var split=source.IndexOf("[ZendiatorComposition(\"shapes\")]",StringComparison.Ordinal);
        var oracle=source.IndexOf("namespace App{public static class Oracle",StringComparison.Ordinal);var text=source[..split]+"}\n"+source[oracle..];return text.Replace("namespace App{","namespace "+(second?"RouteB":"RouteA")+"{");
    }
    private static string MultiRegistration(string profile)
    {
        string Handler(bool b){var k=MultiShape(profile,b);var n=b?"RouteB":"RouteA";return "c.Handlers<"+n+".Ping>(DispatchFamily."+(k.Async?"SendAsync":"SendSync")+",HandlerEntry.Session<"+n+".Handler,"+n+".Factory>());";}
        string Pipeline(bool b){var k=MultiShape(profile,b);var n=b?"RouteB":"RouteA";return "c.Pipeline<"+n+".Ping>(DispatchFamily."+(k.Async?"SendAsync":"SendSync")+","+(k.Wrapper?"Stage.Wrapper<"+n+".W>(),":"")+"Stage.Hook<"+n+".Hook>());";}
        return "using System;using System.Threading;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;namespace App{[ZendiatorComposition(\"multi\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{"+Handler(false)+Handler(true)+Pipeline(true)+Pipeline(false)+"});public static object DispatchA(object m,object r,CancellationToken t)=>((global::App.Generated.Reads.IZendiator)m)."+(MultiShape(profile,false).Async?"SendAsync":"Send")+"((RouteA.Ping)r,t);public static object DispatchB(object m,object r,CancellationToken t)=>((global::App.Generated.Reads.IZendiator)m)."+(MultiShape(profile,true).Async?"SendAsync":"Send")+"((RouteB.Ping)r,t);}}";
    }
    private static MultiFixture MultiRuntimeFixture(string profile)=>MultiFixtures.GetOrAdd(profile,k=>new(()=>{
        var sources=new[]{MultiRouteSource(k,false),MultiRouteSource(k,true),MultiRegistration(k)};
        var compilation=SyncCompilation("", "Multi_"+Guid.NewGuid().ToString("N")).RemoveAllSyntaxTrees().AddSyntaxTrees(sources.Select(s=>Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(s,SyncParseOptions)));
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(compilation,out var output,out var diagnostics);Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var generated=driver.GetRunResult().Results.Single().GeneratedSources;AssertCanonicalOutputRoles(generated,routes:2);
        var body=CanonicalCompositionSource(generated);foreach(var route in generated.Where(IsCanonicalRoute)){var text=route.SourceText.ToString();Assert.DoesNotContain("GetAwaiter",text);Assert.DoesNotContain("ServiceProviderEngineScope",text);Assert.DoesNotContain("GetType()",text);}
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))){var p=Path.Combine(MultiArtifactRoot,"generated-artifacts","multi-"+k);Directory.CreateDirectory(p);for(var i=0;i<sources.Length;i++)File.WriteAllText(Path.Combine(p,"input"+i+".cs"),sources[i]);foreach(var s in generated)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=0,routes=2,handlerOrder="A,B",pipelineOrder="B,A"}));}
        bytes.Position=0;var a=AssemblyLoadContext.Default.LoadFromStream(bytes);var bridge=a.GetType("App.Bridge")!;var mediator=a.GetType("App.Generated.Reads.Zendiator")!;var register=bridge.GetMethod("Register")!;
        ManagedShapeFixture Route(bool second){var q=MultiShape(k,second);var n=second?"RouteB":"RouteA";return new(a,mediator,a.GetType(n+".Ping")!,register,bridge.GetMethod(second?"DispatchB":"DispatchA")!,a.GetType(n+".Oracle")!.GetMethod("Send")!,q.Result=="array"?typeof(int[]):q.Result=="nullable"?typeof(int?):a.GetType(n+".Reply")!,CanonicalRouteSource(generated,n+".Ping"));}
        return new(a,mediator,register,Route(false),Route(true),body);
    })).Value;
    [Theory][InlineData("mixed")][InlineData("same-family")][InlineData("wrappers")]
    public async Task Multi_route_reuses_native_backends_and_separates_helpers_registration_and_selection(string profile)
    {var f=MultiRuntimeFixture(profile);foreach(var second in new[]{false,true}){var k=MultiShape(profile,second);foreach(var generated in new[]{false,true})await RunManagedShape(second?f.B:f.A,k.Async,k.Struct,k.Result,k.Wrapper,generated,false,false);}}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Multi_route_pending_or_before_cancel_does_not_activate_other_route(bool cancel)
    {var f=MultiRuntimeFixture("mixed");foreach(var generated in new[]{false,true})await RunManagedShape(f.B,true,false,"array",true,generated,!cancel,cancel);}
    [Fact]
    public void Multi_route_invalid_second_route_cannot_partially_register_first_route()
    {
        var source=MultiRegistration("mixed").Replace("Stage.Hook<RouteB.Hook>()","Stage.Hook<RouteA.Hook>()");var parts=new[]{MultiRouteSource("mixed",false),MultiRouteSource("mixed",true),source};
        var compilation=SyncCompilation("","MultiInvalid").RemoveAllSyntaxTrees().AddSyntaxTrees(parts.Select(s=>Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(s,SyncParseOptions)));
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(compilation,out var output,out var diagnostics);Assert.Contains(diagnostics,d=>d.Id=="ZEN0026");AssertNoCanonicalOutput(driver.GetRunResult().Results.Single().GeneratedSources);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error&&d.Id!="CS0234"));
        MultiEvidence("multi-boundaries","invalid-route",new{noPartialExecutionOrComposition=true,diagnostics=diagnostics.Select(d=>d.GetMessage())});
    }
    [Fact]
    public void Multi_route_null_provider_and_null_collection_guards_remain()
    {
        var f=MultiRuntimeFixture("mixed");var constructor=Assert.Throws<TargetInvocationException>(()=>Activator.CreateInstance(f.Mediator,new object?[]{null}));Assert.IsType<ArgumentNullException>(constructor.InnerException);
        var registration=Assert.Throws<TargetInvocationException>(()=>f.Register.Invoke(null,new object?[]{null}));Assert.IsType<ArgumentNullException>(registration.InnerException);
        MultiEvidence("multi-boundaries","null-guards",new{providerNullRejected=true,collectionNullRejected=true});
    }
    [Fact]
    public void Multi_route_single_backend_generation_preserves_execution_body_with_standard_provider_contract()
    {SyncRuntimeFixture("owned",false);AsyncRuntimeFixture("direct",false);StandaloneFixture(true,false,"owned",false);WrapperHookRuntimeFixture(true,false,false,"owned");}
}
