using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record ExplicitMetadataFixture(ManagedShapeFixture Runtime,MetadataReference Reference,string ProducerSource);
    private static readonly ConcurrentDictionary<(bool Async,bool Struct,string Result,bool Wrapper),Lazy<ExplicitMetadataFixture>> ExplicitMetadataFixtures=new();
    private static byte[] CompileExplicitMetadataProducer(string source,string name)
    {
        var compilation=SyncCompilation(source,"ExplicitMetadata_"+name+"_"+Guid.NewGuid().ToString("N"));
        Assert.Empty(compilation.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var stream=new MemoryStream();var emitted=compilation.Emit(stream);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));
        var bytes=stream.ToArray();var evidence=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if(!string.IsNullOrEmpty(evidence)){var p=Path.Combine(evidence,"metadata-producers",name);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);File.WriteAllBytes(Path.Combine(p,"producer.dll"),bytes);File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,producerGeneratorExecuted=false,warnings=0,errors=0,generatedCatalog=false,explicitKnownType=true}));}
        return bytes;
    }
    private static ExplicitMetadataFixture ExplicitMetadataRuntimeFixture(bool async,bool valueRequest,string result,bool wrapper)=>ExplicitMetadataFixtures.GetOrAdd((async,valueRequest,result,wrapper),k=>new(()=>{
        var source=ManagedShapeSource(k.Async,k.Struct,k.Result,k.Wrapper);var split=source.IndexOf("[ZendiatorComposition(\"shapes\")]",StringComparison.Ordinal);var ns=source.IndexOf("namespace App{",StringComparison.Ordinal);Assert.True(split>ns);
        // No producer generator/catalog is needed for the existing-DLL explicit fallback.
        var producerSource=source[..split].Replace("[ZendiatorHandler]","")+"}";
        var name=k.Async+"-"+k.Struct+"-"+k.Result+"-"+k.Wrapper;var producerBytes=CompileExplicitMetadataProducer(producerSource,name);
        var reference=MetadataReference.CreateFromImage(producerBytes);using var producerStream=new MemoryStream(producerBytes);var producer=AssemblyLoadContext.Default.LoadFromStream(producerStream);
        var consumer=source[..ns]+"namespace App{"+source[split..];
        if(k.Async&&k.Struct)consumer=consumer.Replace("c=>{c.Handlers","c=>{c.AddHandler<Handler>();c.Handlers");
        var compilation=SyncCompilation(consumer,"MetadataConsumer_"+Guid.NewGuid().ToString("N")).AddReferences(reference);
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(compilation,out var output,out var diagnostics);Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var sources=driver.GetRunResult().Results.Single().GeneratedSources;AssertCanonicalOutputRoles(sources);
        var body=CanonicalDispatchSource(sources);Assert.DoesNotContain("GetAwaiter",body);if(k.Struct)Assert.DoesNotContain("ArgumentNullException.ThrowIfNull(request)",body);if(!k.Async)Assert.DoesNotContain("ValueTask",body);
        var evidence=Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if(!string.IsNullOrEmpty(evidence)){var p=Path.Combine(evidence,"generated-artifacts","explicit-metadata-"+name);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),consumer);foreach(var s in sources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=diagnostics.Length,producerCatalog=false,producerMarker=false,explicitKnownTypes=true}));}
        bytes.Position=0;var a=AssemblyLoadContext.Default.LoadFromStream(bytes);var bridge=a.GetType("App.Bridge")!;
        var runtime=new ManagedShapeFixture(a,a.GetType("App.Generated.Reads.Zendiator")!,producer.GetType("App.Ping")!,bridge.GetMethod("Register")!,bridge.GetMethod("Dispatch")!,a.GetType("App.Oracle")!.GetMethod("Send")!,k.Result=="array"?typeof(int[]):k.Result=="nullable"?typeof(int?):producer.GetType("App.Reply")!,body);
        return new(runtime,reference,producerSource);
    })).Value;
    [Theory][InlineData(false,false,"array",false)][InlineData(true,true,"nullable",false)][InlineData(false,true,"large",true)][InlineData(true,false,"array",true)]
    public async Task Explicit_metadata_known_request_handler_hook_wrapper_factory_match_typed_reference(bool async,bool valueRequest,string result,bool wrapper)
    {var f=ExplicitMetadataRuntimeFixture(async,valueRequest,result,wrapper).Runtime;foreach(var generated in new[]{false,true})await RunManagedShape(f,async,valueRequest,result,wrapper,generated,false,false);}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Explicit_metadata_pending_value_task_and_before_cancel_preserve_owned_cleanup(bool cancel)
    {var f=ExplicitMetadataRuntimeFixture(true,true,"nullable",false).Runtime;foreach(var generated in new[]{false,true})await RunManagedShape(f,true,true,"nullable",false,generated,!cancel,cancel);}
    [Theory][InlineData("missing-contract")][InlineData("duplicate-entry")][InlineData("unmarked-source")]
    public void Explicit_metadata_fallback_preserves_contract_duplicate_and_source_marker_boundaries(string shape)
    {
        var f=ExplicitMetadataRuntimeFixture(true,false,"array",true);var reference=f.Reference;
        if(shape=="missing-contract"){var invalid=f.ProducerSource.Replace("IRequestHandler<Ping,int[]>,","");Assert.NotEqual(invalid,f.ProducerSource);reference=MetadataReference.CreateFromImage(CompileExplicitMetadataProducer(invalid,"invalid-contract"));}
        var local=shape=="unmarked-source"?"public sealed class LocalHandler:IRequestHandler<App.Ping,int[]>{public ValueTask<int[]> HandleAsync(App.Ping r,CancellationToken t)=>new(new[]{r.Value});}":"";
        var entries=shape=="unmarked-source"?"typeof(LocalHandler)":shape=="duplicate-entry"?"typeof(App.Handler),typeof(App.Handler)":"typeof(App.Handler)";
        var source="using System.Threading;using System.Threading.Tasks;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;namespace Consumer{"+local+"[ZendiatorComposition(\"metadata-boundary\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>c.Handlers<App.Ping>(DispatchFamily.SendAsync,"+entries+"));}}";
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"MetadataBoundary_"+Guid.NewGuid().ToString("N")).AddReferences(reference),out var output,out var diagnostics);
        Assert.NotEmpty(diagnostics);Assert.All(diagnostics,d=>Assert.Equal("ZEN0035",d.Id));Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources,s=>s.HintName.Contains("Canonical",StringComparison.Ordinal));
        WriteNestedHookEvidence("explicit-metadata-boundaries",shape,new{shape,source,diagnostics=diagnostics.Select(d=>new{d.Id,message=d.GetMessage()}),generatedExecution=false,explicitKnownType=true});
    }
    [Fact]
    public void Explicit_metadata_preserves_accepted_source_generated_bodies_without_runtime_replay()
    {StandaloneFixture(true,false,"owned",false);WrapperHookRuntimeFixture(true,false,false,"owned");AsyncRuntimeFixture("direct",false);}
}
