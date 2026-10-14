using System.Text.Json;
using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Theory][InlineData("major","UnknownCatalogMajor")][InlineData("algorithm","UnknownFingerprintAlgorithm")][InlineData("feature","UnknownRequiredCatalogFeature")]
    [InlineData("producer","CatalogProducerMismatch")][InlineData("stale","StaleCatalogContract")][InlineData("foreign","CatalogForeignType")][InlineData("duplicate","DuplicateCatalogExport")]
    [InlineData("non-public","CatalogHandlerNotPublic")][InlineData("missing","MissingCatalog")][InlineData("malformed","MalformedCatalog")][InlineData("foreign-contract","MissingHandlerContract")]
    public void Catalog_v1_rejects_schema_identity_contract_visibility_and_duplicate_exports(string shape,string reason)
    {
        var f=CatalogRuntimeFixture(true,false);var source=f.ProducerSource;var consumerName="CatalogBoundary_"+Guid.NewGuid().ToString("N");
        if(shape=="non-public"){source=source.Replace("public sealed class Handler:","internal sealed class Handler:").Replace("public sealed class Factory:","internal sealed class Factory:");source=source.Insert(source.IndexOf("namespace App{",StringComparison.Ordinal),"[assembly:global::System.Runtime.CompilerServices.InternalsVisibleTo(\""+consumerName+"\")]\n");}
        if(shape=="foreign-contract")source=source.Replace("IRequestHandler<Ping,int[]>,","Fake.IRequestHandler<Ping,int[]>,")+"namespace Fake{public interface IRequestHandler<R,T>{ValueTask<T> HandleAsync(R r,CancellationToken t);}}";
        var assemblyName="CatalogInvalid_"+shape.Replace('-','_')+"_"+Guid.NewGuid().ToString("N");var initial=SyncCompilation(source,assemblyName);var handler=initial.GetTypeByMetadataName("App.Handler")!;
        var fingerprint=CatalogV1Reference.Fingerprint(initial,handler);var header="[assembly:global::Zendiator.ZendiatorGeneratedCatalogAttribute("+(shape=="major"?"2":"1")+",0,"+JsonSerializer.Serialize(shape=="producer"?"wrong":initial.Assembly.Identity.ToString())+","+JsonSerializer.Serialize(shape=="algorithm"?"future":CatalogSignatureV1.Algorithm)+(shape=="feature"?",RequiredFeatures=new[]{\"future\"}":shape=="malformed"?",RequiredFeatures=null!":"")+")]";
        var export="[assembly:global::Zendiator.ZendiatorHandlerExportAttribute(typeof("+(shape=="foreign"?"global::Zendiator.Generator.Tests.ManagedShapeProbe":"global::App.Handler")+"),\""+(shape=="stale"?new string('0',64):fingerprint)+"\")]";
        if(shape=="duplicate")export+="[assembly:global::Zendiator.ZendiatorHandlerExportAttribute(typeof(global::App.Handler),\""+new string('0',64)+"\")]";
        if(shape!="missing")source=source.Insert(source.IndexOf("namespace App{",StringComparison.Ordinal),header+export+"\n");
        var data=CatalogProducerBytes(source,"invalid-"+shape,assemblyName,false);var reference=MetadataReference.CreateFromImage(data);
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(CatalogConsumerRegistration(),consumerName).AddReferences(reference),out var output,out var diagnostics);
        Assert.All(diagnostics,d=>Assert.Equal("ZEN0035",d.Id));var diagnostic=Assert.Single(diagnostics,d=>d.GetMessage().Contains(reason,StringComparison.Ordinal));Assert.Contains("UseCatalogFrom",diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));AssertNoCanonicalOutput(driver.GetRunResult().Results.Single().GeneratedSources);
        CatalogEvidence("catalog-boundaries",shape,new{shape,reason,diagnostic=diagnostic.GetMessage(),importLocation=diagnostic.Location.SourceSpan.ToString(),generatedExecution=false});
    }
    [Fact]
    public void Catalog_v1_duplicate_selection_reports_second_and_original_import()
    {
        var f=CatalogRuntimeFixture(true,false);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(CatalogConsumerRegistration(true),"CatalogDuplicate_"+Guid.NewGuid().ToString("N")).AddReferences(f.Reference),out var output,out var diagnostics);
        var d=Assert.Single(diagnostics);Assert.Contains("DuplicateCatalogSelection",d.GetMessage());Assert.Single(d.AdditionalLocations);Assert.Contains("UseCatalogFrom<App.Ping>",d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan));Assert.Contains("UseCatalogFrom<App.CatalogMarker>",d.AdditionalLocations[0].SourceTree!.GetText().ToString(d.AdditionalLocations[0].SourceSpan));
        Assert.Empty(output.GetDiagnostics().Where(x=>x.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));AssertNoCanonicalOutput(driver.GetRunResult().Results.Single().GeneratedSources);
        var configuration=new Zendiator.DependencyInjection.ZendiatorConfiguration();configuration.UseCatalogFrom<string>();Assert.Throws<InvalidOperationException>(()=>configuration.UseCatalogFrom<object>());var snapshot=configuration.Snapshot();Assert.Single(snapshot.CatalogMarkers);Assert.Throws<InvalidOperationException>(()=>configuration.UseCatalogFrom<GeneratorTests>());
        CatalogEvidence("catalog-boundaries","selection",new{second=d.Location.SourceSpan.ToString(),original=d.AdditionalLocations[0].SourceSpan.ToString(),runtimeDuplicateAndFreeze=true});
    }
    [Fact]
    public void Catalog_v1_no_friend_does_not_emit_internal_receiver_route()
    {
        var f=CatalogRuntimeFixture(true,true);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(CatalogConsumerRegistration(),"CatalogDenied_"+Guid.NewGuid().ToString("N")).AddReferences(f.Reference),out var output,out var diagnostics);
        Assert.Contains(output.GetDiagnostics(),d=>d.Id=="CS0122");AssertNoCanonicalOutput(driver.GetRunResult().Results.Single().GeneratedSources);
        CatalogEvidence("catalog-boundaries","no-friend",new{compilerOwnsAccessibility=true,generatedExecution=false,diagnostics=diagnostics.Select(d=>d.GetMessage())});
    }
    [Fact]
    public void Catalog_v1_JSON_encoding_matches_independent_SystemTextJson_reference()
    {
        object?[] values=[string.Concat(Enumerable.Range(0,128).Select(i=>(char)i)),"日本語 é \ud83d\ude00 \ud800 \udfff",new object[]{"assembly + nested",7,true,false,new[]{"Nullable","constraint<T>","\"\\\n"}},null];
        foreach(var value in values)Assert.Equal(JsonSerializer.Serialize(value),CatalogSignatureV1.Json(value));
        CatalogEvidence("catalog-schema","encoding",new{reference="H01 System.Text.Json default; ASCII128, Unicode/surrogate, nested typed arrays",matched=true});
    }
    [Fact]
    public void Catalog_v1_producer_rejects_duplicate_source_exports_before_metadata_dedup()
    {
        var source="using System.Threading;using Zendiator;[assembly:ZendiatorHandlerExport(typeof(App.Handler),\""+new string('0',64)+"\")][assembly:ZendiatorHandlerExport(typeof(App.Handler),\""+new string('1',64)+"\")]namespace App{public sealed record Ping:ISyncRequest<int>;[ZendiatorHandler]public sealed class Handler:ISyncRequestHandler<Ping,int>{public int Handle(Ping r,CancellationToken t)=>1;}}";
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"CatalogProducerDuplicate"),out var output,out var diagnostics);var d=Assert.Single(diagnostics);Assert.Contains("DuplicateCatalogExport",d.GetMessage());Assert.Single(d.AdditionalLocations);Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName==GeneratedHandlerCatalog.HintName);
        Assert.Empty(output.GetDiagnostics().Where(x=>x.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));CatalogEvidence("catalog-schema","producer-duplicate",new{source,diagnostic=d.GetMessage(),second=d.Location.SourceSpan.ToString(),original=d.AdditionalLocations[0].SourceSpan.ToString()});
    }
    [Fact]
    public void Catalog_v1_body_edit_keeps_catalog_source_cached_contract_edit_changes_fingerprint()
    {
        var source="using System.Threading;using Zendiator;namespace App{public sealed record Ping:ISyncRequest<int>;[ZendiatorHandler]public sealed class Handler:ISyncRequestHandler<Ping,int>{public int Handle(Ping r,CancellationToken t)=>1;}}";
        var driver=SyncDriver().RunGenerators(SyncCompilation(source,"CatalogCache"));var first=driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName==GeneratedHandlerCatalog.HintName).SourceText.ToString();
        driver=driver.RunGenerators(SyncCompilation(source.Replace("=>1","=>2"),"CatalogCache"));Assert.Equal(first,driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName==GeneratedHandlerCatalog.HintName).SourceText.ToString());Assert.All(driver.GetRunResult().Results.Single().TrackedSteps["GeneratedHandlerCatalogSource"].SelectMany(s=>s.Outputs),o=>Assert.Equal(IncrementalStepRunReason.Cached,o.Reason));
        driver=driver.RunGenerators(SyncCompilation(source.Replace("<int>","<long>").Replace("<Ping,int>","<Ping,long>").Replace("public int Handle","public long Handle"),"CatalogCache"));Assert.NotEqual(first,driver.GetRunResult().Results.Single().GeneratedSources.Single(s=>s.HintName==GeneratedHandlerCatalog.HintName).SourceText.ToString());
        CatalogEvidence("catalog-schema","cache",new{bodyCached=true,contractFingerprintChanged=true});
    }
    [Fact]
    public void Catalog_v1_preserves_accepted_source_canonical_bodies_without_runtime_replay()
    {StandaloneFixture(true,false,"owned",false);WrapperHookRuntimeFixture(true,false,false,"owned");AsyncRuntimeFixture("direct",false);}
}
