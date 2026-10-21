using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private const string IncrementalRouteSource = """
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
    private static GeneratorDriver IncrementalRouteRun(GeneratorDriver driver, string source, string label, bool errors = false)
        => IncrementalRouteRun(driver, source, label, out _, errors);
    private static GeneratorDriver IncrementalRouteRun(GeneratorDriver driver, string source, string label, out Compilation output, bool errors = false)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "FineIncremental"), out output, out var diagnostics);
        Assert.Null(driver.GetRunResult().Results.Single().Exception);
        if (!errors) { Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); }
        var run = driver.GetRunResult().Results.Single();
        var sync = (CanonicalSyncEmissionModel)Assert.Single(run.TrackedSteps["CanonicalSyncEmissionModel"].SelectMany(s => s.Outputs)).Value;
        var async = (CanonicalAsyncEmissionModel)Assert.Single(run.TrackedSteps["CanonicalAsyncEmissionModel"].SelectMany(s => s.Outputs)).Value;
        var wrappers = (CanonicalWrapperEmissionModel)Assert.Single(run.TrackedSteps["CanonicalWrapperEmissionModel"].SelectMany(s => s.Outputs)).Value;
        var capabilities = (CanonicalCapabilityEmissionModel)Assert.Single(run.TrackedSteps["CanonicalCapabilityEmissionModel"].SelectMany(s => s.Outputs)).Value;
        var multi = (CanonicalMultiEmissionModel)Assert.Single(run.TrackedSteps["CanonicalMultiEmissionModel"].SelectMany(s => s.Outputs)).Value;
        // H05 intentionally splits these logical bodies. Compilation above validates the actual partial outputs.
        Assert.Equal(CanonicalSplitSourceEmitter.Accepted(sync, async, wrappers, capabilities, multi).Count,
            run.GeneratedSources.Count(s => s.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal)));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))) { var p = Path.Combine(CatalogArtifactRoot, "incremental-route-snapshots", label); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "input.cs"), source); foreach (var s in run.GeneratedSources) File.WriteAllText(Path.Combine(p, s.HintName), s.SourceText.ToString()); }
        return driver;
    }
    private static Dictionary<string, IncrementalStepRunReason> RouteReasons(GeneratorDriver driver, string step) => driver.GetRunResult().Results.Single().TrackedSteps[step].SelectMany(s => s.Outputs).ToDictionary(o => o.Value switch { CanonicalRouteInput i => i.Route.RequestName, CanonicalRouteGraph g => g.Input.Route.RequestName, CanonicalRouteBackend b => b.Route.Input.Route.RequestName, CanonicalRouteText t => t.RequestName, _ => throw new InvalidOperationException() }, o => o.Reason);
    [Fact]
    public void Incremental_route_body_and_unrelated_type_edits_keep_graph_and_template_cached()
    {
        var driver = IncrementalRouteRun(SyncDriver(), IncrementalRouteSource, "unrelated-before"); var changed = IncrementalRouteSource.Replace("Handle(A r,CancellationToken t)=>r.Value", "Handle(A r,CancellationToken t)=>r.Value+1") + "namespace Unrelated{public sealed class Noise{public string Text=>\"noise\";}}";
        driver = IncrementalRouteRun(driver, changed, "unrelated-after"); foreach (var step in new[] { "CanonicalRouteGraphEntry", "CanonicalRouteBackendEntry", "CanonicalRouteSourceEntry" }) Assert.All(RouteReasons(driver, step).Values, r => Assert.Equal(IncrementalStepRunReason.Cached, r));
        CatalogEvidence("incremental-route-observations", "unrelated", new { graphCached = true, templateCached = true, bodyAndUnrelatedEdit = true, routeOutputCountMatchesAcceptedModel = true });
    }
    [Fact]
    public void Incremental_route_contract_edit_only_regenerates_affected_route()
    {
        var driver = IncrementalRouteRun(SyncDriver(), IncrementalRouteSource, "contract-before"); var changed = IncrementalRouteSource.Replace("A(int Value):ISyncRequest<int>", "A(int Value):ISyncRequest<long>").Replace("ISyncRequestHandler<A,int>", "ISyncRequestHandler<A,long>").Replace("public int Handle(A", "public long Handle(A"); driver = IncrementalRouteRun(driver, changed, "contract-after");
        foreach (var step in new[] { "CanonicalRouteGraphEntry", "CanonicalRouteBackendEntry", "CanonicalRouteSourceEntry" }) { var reasons = RouteReasons(driver, step); Assert.Equal(IncrementalStepRunReason.Modified, reasons["global::Fine.A"]); Assert.Equal(IncrementalStepRunReason.Cached, reasons["global::Fine.B"]); }
        CatalogEvidence("incremental-route-observations", "contract", new { affectedModified = true, unrelatedCached = true, routeOutputCountMatchesAcceptedModel = true, generatedCompilationChecked = true });
    }
    [Fact]
    public void Incremental_route_current_diagnostic_locations_refresh_without_template_work()
    {
        var source = IncrementalRouteSource.Replace("c.Handlers<A>(DispatchFamily.SendSync,typeof(HA));", "").Replace("c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));", "c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));c.Pipeline<B>(DispatchFamily.SendSync,Stage.Hook<Bad>());") + "namespace Fine{public sealed class Bad:IAsyncSendRequestTransform<B>{public ValueTask<B> BeforeAsync(B r,CancellationToken t)=>new(r);}}";
        var driver = IncrementalRouteRun(SyncDriver(), source, "location-before", errors: true); var initial = Assert.Single(driver.GetRunResult().Diagnostics); var moved = "\n\n" + source; driver = IncrementalRouteRun(driver, moved, "location-after", errors: true); var current = Assert.Single(driver.GetRunResult().Diagnostics); Assert.Equal(initial.Id, current.Id); Assert.Equal(initial.Location.SourceSpan.Start + 2, current.Location.SourceSpan.Start); Assert.StartsWith("\n\n", current.Location.SourceTree!.GetText().ToString());
        var run = driver.GetRunResult().Results.Single();
        AssertNoCanonicalOutput(run.GeneratedSources);
        if (run.TrackedSteps.TryGetValue("CanonicalRouteSourceEntry", out var steps)) Assert.Empty(steps.SelectMany(s => s.Outputs));
        CatalogEvidence("incremental-route-observations", "locations", new { spanDelta = 2, diagnosticLocationRefreshed = true, invalidCompositionHasNoTemplateOutput = true });
    }
    [Fact]
    public void Incremental_route_multiple_compositions_keep_unrelated_composition_cached()
    {
        var source = IncrementalRouteSource.Replace("[ZendiatorComposition(\"fine\")]public sealed class Reads;", "[ZendiatorComposition(\"fine\")]public sealed class Reads;[ZendiatorComposition(\"other\")]public sealed class Other;").Replace("c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));", "").Replace("});}", "});public static IServiceCollection RegisterOther(IServiceCollection s)=>s.AddZendiator<Other>(c=>c.Handlers<B>(DispatchFamily.SendSync,typeof(HB)));}");
        var driver = IncrementalRouteRun(SyncDriver(), source, "compositions-before"); var changed = source.Replace("A(int Value):ISyncRequest<int>", "A(int Value):ISyncRequest<long>").Replace("ISyncRequestHandler<A,int>", "ISyncRequestHandler<A,long>").Replace("public int Handle(A", "public long Handle(A"); driver = IncrementalRouteRun(driver, changed, "compositions-after"); var reasons = RouteReasons(driver, "CanonicalRouteSourceEntry"); Assert.Equal(IncrementalStepRunReason.Modified, reasons["global::Fine.A"]); Assert.Equal(IncrementalStepRunReason.Cached, reasons["global::Fine.B"]); CatalogEvidence("incremental-route-observations", "compositions", new { compositionCount = 2, unrelatedTemplateCached = true, routeOutputCountMatchesAcceptedModel = true, generatedCompilationChecked = true });
    }
    private static bool IsCanonicalRoute(GeneratedSourceResult source) => source.HintName.StartsWith("Zendiator.Route.", StringComparison.Ordinal);
    private static bool IsCanonicalComposition(GeneratedSourceResult source) => source.HintName.StartsWith("Zendiator.Composition.", StringComparison.Ordinal);
    private static string CanonicalRouteSource(IEnumerable<GeneratedSourceResult> sources, string? request = null)
        => Assert.Single(sources, s => IsCanonicalRoute(s) && (request == null || s.SourceText.ToString().Contains("global::" + request + " request", StringComparison.Ordinal))).SourceText.ToString();
    private static string CanonicalCompositionSource(IEnumerable<GeneratedSourceResult> sources)
        => Assert.Single(sources, IsCanonicalComposition).SourceText.ToString();
    private static string CanonicalDispatchSource(IEnumerable<GeneratedSourceResult> sources)
        => CanonicalRouteSource(sources) + CanonicalCompositionSource(sources);
    private static void AssertCanonicalOutputRoles(IEnumerable<GeneratedSourceResult> sources, int routes = 1, int compositions = 1, int interceptors = 1, bool legacy = false)
    {
        var files = sources.ToArray(); var execution = files.Where(IsCanonicalRoute).ToArray(); var shared = files.Where(IsCanonicalComposition).ToArray();
        Assert.Equal(routes, execution.Length); Assert.Equal(compositions, shared.Length);
        Assert.Equal(interceptors, files.Count(s => s.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal)));
        var catalogs = files.Where(s => s.HintName == "Zendiator.GeneratedCatalog.g.cs").ToArray();
        Assert.InRange(catalogs.Length, 0, 1);
        Assert.All(catalogs, s => { Assert.Contains("ZendiatorGeneratedCatalogAttribute", s.SourceText.ToString()); Assert.Contains("ZendiatorHandlerExportAttribute", s.SourceText.ToString()); });
        Assert.All(execution, s => { Assert.Contains("partial interface IZendiator", s.SourceText.ToString()); Assert.DoesNotContain("ZendiatorGeneratedRegistrar", s.SourceText.ToString()); });
        Assert.All(shared, s => { Assert.Contains("ZendiatorGeneratedRegistrar", s.SourceText.ToString()); Assert.DoesNotContain("interface IZendiator", s.SourceText.ToString()); });
        var legacyFiles = files.Where(s => s.HintName == "Zendiator.g.cs").ToArray();
        Assert.Equal(legacy ? 1 : 0, legacyFiles.Length);
        Assert.All(legacyFiles, s => Assert.Contains("partial class Zendiator", s.SourceText.ToString()));
        Assert.All(files, s => Assert.True((legacy && s.HintName == "Zendiator.g.cs") || IsCanonicalRoute(s) || IsCanonicalComposition(s) || s.HintName.StartsWith("Zendiator.Dispatch.", StringComparison.Ordinal) || s.HintName.EndsWith(".Interceptors.g.cs", StringComparison.Ordinal) || s.HintName == "Zendiator.GeneratedCatalog.g.cs", s.HintName));
    }
    private static void AssertNoCanonicalOutput(IEnumerable<GeneratedSourceResult> sources)
        => Assert.DoesNotContain(sources, s => IsCanonicalRoute(s) || IsCanonicalComposition(s));
    private static Dictionary<string, string> SplitFiles(GeneratorDriver driver) => driver.GetRunResult().Results.Single().GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString());
    private static string SplitRouteHint(Dictionary<string, string> files, string request) => files.Single(p => p.Key.StartsWith("Zendiator.Route.", StringComparison.Ordinal) && p.Value.Contains("Send(global::Fine." + request + " request", StringComparison.Ordinal)).Key;
    [Fact]
    public void Split_route_contract_change_keeps_stable_id_and_unaffected_output()
    {
        var driver = IncrementalRouteRun(SyncDriver(), IncrementalRouteSource, "split-contract-before"); var before = SplitFiles(driver); var a = SplitRouteHint(before, "A"); var b = SplitRouteHint(before, "B"); var changed = IncrementalRouteSource.Replace("A(int Value):ISyncRequest<int>", "A(int Value):ISyncRequest<long>").Replace("ISyncRequestHandler<A,int>", "ISyncRequestHandler<A,long>").Replace("public int Handle(A", "public long Handle(A"); driver = IncrementalRouteRun(driver, changed, "split-contract-after"); var after = SplitFiles(driver);
        Assert.Equal(a, SplitRouteHint(after, "A")); Assert.Equal(b, SplitRouteHint(after, "B")); Assert.NotEqual(before[a], after[a]); Assert.Equal(before[b], after[b]); Assert.Contains("long Send", after[a]); Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["CanonicalRouteOutputFile"].SelectMany(s => s.Outputs).Where(o => ((CanonicalSourceFile)o.Value).HintName == b), o => Assert.True(o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged)); CatalogEvidence("split-route-observations", "contract", new { stableId = true, unaffectedBytes = true, targetChanged = true });
    }
    [Fact]
    public void Split_route_insertion_and_reordering_preserve_existing_ids_and_update_composition()
    {
        var driver = IncrementalRouteRun(SyncDriver(), IncrementalRouteSource, "split-order-before"); var before = SplitFiles(driver); var a = SplitRouteHint(before, "A"); var b = SplitRouteHint(before, "B"); var shared = before.Single(p => p.Key.StartsWith("Zendiator.Composition.", StringComparison.Ordinal)).Key;
        var changed = IncrementalRouteSource.Replace("c.Handlers<A>(DispatchFamily.SendSync,typeof(HA));c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));", "c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));c.Handlers<C>(DispatchFamily.SendSync,typeof(HC));c.Handlers<A>(DispatchFamily.SendSync,typeof(HA));") + "namespace Fine{public sealed record C(int Value):ISyncRequest<int>;[ZendiatorHandler]public sealed class HC:ISyncRequestHandler<C,int>{public int Handle(C r,CancellationToken t)=>r.Value;}}"; driver = IncrementalRouteRun(driver, changed, "split-order-after"); var after = SplitFiles(driver); Assert.Equal(before[a], after[a]); Assert.Equal(before[b], after[b]); Assert.Equal(3, after.Keys.Count(k => k.StartsWith("Zendiator.Route.", StringComparison.Ordinal))); Assert.NotEqual(before[shared], after[shared]); Assert.True(after[shared].IndexOf("Handlers<global::Fine.B>", StringComparison.Ordinal) < after[shared].IndexOf("Handlers<global::Fine.C>", StringComparison.Ordinal)); Assert.True(after[shared].IndexOf("Handlers<global::Fine.C>", StringComparison.Ordinal) < after[shared].IndexOf("Handlers<global::Fine.A>", StringComparison.Ordinal)); CatalogEvidence("split-route-observations", "order", new { stableOldRouteIds = true, oldRouteBytesEqual = true, newRoute = 1, sharedOrderUpdated = true });
    }
    [Fact]
    public void Split_route_shared_stage_dependency_updates_once_without_extra_lookup()
    {
        var source = IncrementalRouteSource + "namespace Fine{public sealed class Shared:ISendFinallyObserver<A>,ISendFinallyObserver<B>{public void Finally(A r,SendExitInfo e,CancellationToken t){}public void Finally(B r,SendExitInfo e,CancellationToken t){}}}"; var driver = IncrementalRouteRun(SyncDriver(), source, "split-dependency-before"); var before = SplitFiles(driver); var shared = before.Single(p => p.Key.StartsWith("Zendiator.Composition.", StringComparison.Ordinal)).Key;
        var changed = source.Replace("c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));", "c.Handlers<B>(DispatchFamily.SendSync,typeof(HB));c.Pipeline<A>(DispatchFamily.SendSync,Stage.Hook<Shared>());c.Pipeline<B>(DispatchFamily.SendSync,Stage.Hook<Shared>());"); driver = IncrementalRouteRun(driver, changed, "split-dependency-after"); var after = SplitFiles(driver); Assert.NotEqual(before[shared], after[shared]); Assert.Equal(1, after[shared].Split("ServiceDescriptor.Transient(typeof(global::Fine.Shared)", StringSplitOptions.None).Length - 1);
        var sharedAccessor = "_provider.Required_" + CanonicalSplitSourceEmitter.Id("global::Fine.Shared") + "()";
        Assert.All(after.Where(p => p.Key.StartsWith("Zendiator.Route.", StringComparison.Ordinal)), p => { Assert.Equal(1, p.Value.Split(sharedAccessor, StringSplitOptions.None).Length - 1); Assert.DoesNotContain("ZendiatorGeneratedRegistrar", p.Value); }); CatalogEvidence("split-route-observations", "dependency", new { sharedDescriptorCount = 1, routeLookupsUnchanged = 1, globalUpdated = true });
    }
    [Fact]
    public void Split_route_position_only_change_preserves_outputs_and_runtime_contract()
    {
        var driver = IncrementalRouteRun(SyncDriver(), IncrementalRouteSource, "split-position-before"); var before = SplitFiles(driver); driver = IncrementalRouteRun(driver, "\n\n" + IncrementalRouteSource, "split-position-after", out var output); var after = SplitFiles(driver); foreach (var pair in before.Where(p => p.Key.StartsWith("Zendiator.Route.", StringComparison.Ordinal) || p.Key.StartsWith("Zendiator.Composition.", StringComparison.Ordinal))) Assert.Equal(pair.Value, after[pair.Key]); Assert.Contains(before.Where(p => p.Key.Contains("Interceptors", StringComparison.Ordinal)), p => p.Value != after[p.Key]);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); bytes.Position = 0; var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(bytes); IServiceCollection services = new ServiceCollection(); assembly.GetType("Fine.Bridge")!.GetMethod("Register")!.Invoke(null, [services]); using var provider = services.BuildServiceProvider(); var mediator = provider.GetRequiredService(assembly.GetType("Fine.Generated.Reads.IZendiator")!); foreach (var name in new[] { "A", "B" }) { var type = assembly.GetType("Fine." + name)!; var request = Activator.CreateInstance(type, [11])!; Assert.Equal(11, mediator.GetType().GetMethod("Send", [type, typeof(CancellationToken)])!.Invoke(mediator, [request, CancellationToken.None])); }
        CatalogEvidence("split-route-observations", "position", new { routeAndSharedBytesEqual = true, interceptorPositionUpdated = true, compileEmit = true, runtimeResults = new[] { 11, 11 } });
    }
}
