using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string IncrementalRouteSource="""
        using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;
        namespace Fine{
        public sealed record A(int Value):ISyncRequest<int>;
        public sealed record B(int Value):ISyncRequest<int>;
        [ZendiatorHandler]public sealed class HA:ISyncRequestHandler<A,int>{public int Handle(A r,CancellationToken t)=>r.Value;}
        [ZendiatorHandler]public sealed class HB:ISyncRequestHandler<B,int>{public int Handle(B r,CancellationToken t)=>r.Value;}
        [ZendiatorComposition("fine")]public sealed class Reads;
        public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{c.Handlers<A>(DispatchFamily.SendSync,typeof(HA));c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));});}
        }
        """;
    private static GeneratorDriver IncrementalRouteRun(GeneratorDriver driver,string source,string label,bool errors=false)
    {
        driver=driver.RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"FineIncremental"),out var output,out var diagnostics);
        Assert.Null(driver.GetRunResult().Results.Single().Exception);
        if(!errors){Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));}
        var run=driver.GetRunResult().Results.Single();
        var sync=(CanonicalSyncEmissionModel)Assert.Single(run.TrackedSteps["CanonicalSyncEmissionModel"].SelectMany(s=>s.Outputs)).Value;
        var async=(CanonicalAsyncEmissionModel)Assert.Single(run.TrackedSteps["CanonicalAsyncEmissionModel"].SelectMany(s=>s.Outputs)).Value;
        var wrappers=(CanonicalWrapperEmissionModel)Assert.Single(run.TrackedSteps["CanonicalWrapperEmissionModel"].SelectMany(s=>s.Outputs)).Value;
        var capabilities=(CanonicalCapabilityEmissionModel)Assert.Single(run.TrackedSteps["CanonicalCapabilityEmissionModel"].SelectMany(s=>s.Outputs)).Value;
        var multi=(CanonicalMultiEmissionModel)Assert.Single(run.TrackedSteps["CanonicalMultiEmissionModel"].SelectMany(s=>s.Outputs)).Value;
        var expected=new Dictionary<string,string>{{"Zendiator.Canonical.SendSync.g.cs",CanonicalSyncSourceEmitter.Emit(sync)},{"Zendiator.Canonical.SendAsync.g.cs",CanonicalAsyncSourceEmitter.Emit(async)},{"Zendiator.Canonical.Wrappers.g.cs",CanonicalWrapperSourceEmitter.Emit(wrappers)},{"Zendiator.Canonical.Capabilities.g.cs",CanonicalCapabilitySourceEmitter.Emit(capabilities)},{"Zendiator.Canonical.Multiple.g.cs",CanonicalMultiSourceEmitter.Emit(multi)}};
        foreach(var pair in expected){var actual=run.GeneratedSources.Where(s=>s.HintName==pair.Key).Select(s=>s.SourceText.ToString()).SingleOrDefault()??"";Assert.Equal(pair.Value,actual);}
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))){var p=Path.Combine(CatalogArtifactRoot,"incremental-route-snapshots",label);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);foreach(var s in run.GeneratedSources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());}
        return driver;
    }
    private static Dictionary<string,IncrementalStepRunReason> RouteReasons(GeneratorDriver driver,string step)=>driver.GetRunResult().Results.Single().TrackedSteps[step].SelectMany(s=>s.Outputs).ToDictionary(o=>o.Value switch{CanonicalRouteInput i=>i.Route.RequestName,CanonicalRouteGraph g=>g.Input.Route.RequestName,CanonicalRouteBackend b=>b.Route.Input.Route.RequestName,CanonicalRouteText t=>t.RequestName,_=>throw new InvalidOperationException()},o=>o.Reason);
    [Fact]
    public void Incremental_route_body_and_unrelated_type_edits_keep_graph_and_template_cached()
    {
        var driver=IncrementalRouteRun(SyncDriver(),IncrementalRouteSource,"unrelated-before");var changed=IncrementalRouteSource.Replace("Handle(A r,CancellationToken t)=>r.Value","Handle(A r,CancellationToken t)=>r.Value+1")+"namespace Unrelated{public sealed class Noise{public string Text=>\"noise\";}}";
        driver=IncrementalRouteRun(driver,changed,"unrelated-after");foreach(var step in new[]{"CanonicalRouteGraphEntry","CanonicalRouteBackendEntry","CanonicalRouteSourceEntry"})Assert.All(RouteReasons(driver,step).Values,r=>Assert.Equal(IncrementalStepRunReason.Cached,r));
        CatalogEvidence("incremental-route-observations","unrelated",new{graphCached=true,templateCached=true,bodyAndUnrelatedEdit=true,generatedBytesEqual=true});
    }
    [Fact]
    public void Incremental_route_contract_edit_only_regenerates_affected_route()
    {
        var driver=IncrementalRouteRun(SyncDriver(),IncrementalRouteSource,"contract-before");var changed=IncrementalRouteSource.Replace("A(int Value):ISyncRequest<int>","A(int Value):ISyncRequest<long>").Replace("ISyncRequestHandler<A,int>","ISyncRequestHandler<A,long>").Replace("public int Handle(A","public long Handle(A");driver=IncrementalRouteRun(driver,changed,"contract-after");
        foreach(var step in new[]{"CanonicalRouteGraphEntry","CanonicalRouteBackendEntry","CanonicalRouteSourceEntry"}){var reasons=RouteReasons(driver,step);Assert.Equal(IncrementalStepRunReason.Modified,reasons["global::Fine.A"]);Assert.Equal(IncrementalStepRunReason.Cached,reasons["global::Fine.B"]);}
        CatalogEvidence("incremental-route-observations","contract",new{affectedModified=true,unrelatedCached=true,wholeOutputMatchesDirectEmitter=true});
    }
    [Fact]
    public void Incremental_route_current_diagnostic_locations_refresh_without_template_work()
    {
        var source=IncrementalRouteSource.Replace("c.Handlers<A>(DispatchFamily.SendSync,typeof(HA));","").Replace("c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));","c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));c.Pipeline<B>(DispatchFamily.SendSync,Stage.Hook<Bad>());")+"namespace Fine{public sealed class Bad:IAsyncSendRequestTransform<B>{public ValueTask<B> BeforeAsync(B r,CancellationToken t)=>new(r);}}";
        var driver=IncrementalRouteRun(SyncDriver(),source,"location-before",errors:true);var initial=Assert.Single(driver.GetRunResult().Diagnostics);var moved="\n\n"+source;driver=IncrementalRouteRun(driver,moved,"location-after",errors:true);var current=Assert.Single(driver.GetRunResult().Diagnostics);Assert.Equal(initial.Id,current.Id);Assert.Equal(initial.Location.SourceSpan.Start+2,current.Location.SourceSpan.Start);Assert.StartsWith("\n\n",current.Location.SourceTree!.GetText().ToString());
        Assert.All(RouteReasons(driver,"CanonicalRouteSourceEntry").Values,r=>Assert.Equal(IncrementalStepRunReason.Cached,r));CatalogEvidence("incremental-route-observations","locations",new{spanDelta=2,diagnosticLocationRefreshed=true,templateCached=true});
    }
    [Fact]
    public void Incremental_route_multiple_compositions_keep_unrelated_composition_cached()
    {
        var source=IncrementalRouteSource.Replace("[ZendiatorComposition(\"fine\")]public sealed class Reads;","[ZendiatorComposition(\"fine\")]public sealed class Reads;[ZendiatorComposition(\"other\")]public sealed class Other;").Replace("c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));","").Replace("});}","});public static IServiceCollection RegisterOther(IServiceCollection s)=>s.AddZendiator<Other>(c=>c.Handlers<B>(DispatchFamily.SendSync,typeof(HB)));}");
        var driver=IncrementalRouteRun(SyncDriver(),source,"compositions-before");var changed=source.Replace("A(int Value):ISyncRequest<int>","A(int Value):ISyncRequest<long>").Replace("ISyncRequestHandler<A,int>","ISyncRequestHandler<A,long>").Replace("public int Handle(A","public long Handle(A");driver=IncrementalRouteRun(driver,changed,"compositions-after");var reasons=RouteReasons(driver,"CanonicalRouteSourceEntry");Assert.Equal(IncrementalStepRunReason.Modified,reasons["global::Fine.A"]);Assert.Equal(IncrementalStepRunReason.Cached,reasons["global::Fine.B"]);CatalogEvidence("incremental-route-observations","compositions",new{compositionCount=2,unrelatedTemplateCached=true,generatedBytesMatchDirectEmitter=true});
    }
}
