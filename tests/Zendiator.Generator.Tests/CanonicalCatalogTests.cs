using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record CatalogFixture(ManagedShapeFixture Runtime,MetadataReference Reference,string ProducerSource,bool Async,bool ValueRequest,string Result,bool Wrapper,string ConsumerName);
    private static readonly ConcurrentDictionary<(bool Async,bool Friend),Lazy<CatalogFixture>> CatalogFixtures=new();
    private static string CatalogArtifactRoot=>Path.GetDirectoryName(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))!;
    private static void CatalogEvidence(string folder,string name,object value)
    {if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR")))return;var p=Path.Combine(CatalogArtifactRoot,folder);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,name+".json"),JsonSerializer.Serialize(value));}
    private static byte[] CatalogProducerBytes(string source,string name,string assemblyName,bool generate)
    {
        var original=SyncCompilation(source,assemblyName);Compilation output=original;GeneratorDriver? driver=null;
        if(generate){driver=SyncDriver().RunGeneratorsAndUpdateCompilation(original,out output,out var diagnostics);Assert.Empty(diagnostics);}
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));
        var exports=output.Assembly.GetAttributes().Where(a=>a.AttributeClass?.ToDisplayString()=="Zendiator.ZendiatorHandlerExportAttribute").ToArray();
        if(generate){
            Assert.Equal("Zendiator.GeneratedCatalog.g.cs",Assert.Single(driver!.GetRunResult().Results.Single().GeneratedSources).HintName);
            Assert.Single(exports);var export=exports[0];var type=(INamedTypeSymbol)export.ConstructorArguments[0].Value!;
            Assert.Equal(CatalogV1Reference.Fingerprint(original,original.GetTypeByMetadataName(SymbolUtilities.QualifiedMetadataName(type.OriginalDefinition)!)!),export.ConstructorArguments[1].Value);
        }
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))){
            var p=Path.Combine(CatalogArtifactRoot,"catalog-producers",name);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),source);File.WriteAllBytes(Path.Combine(p,"producer.dll"),bytes.ToArray());if(driver!=null)foreach(var s in driver.GetRunResult().Results.Single().GeneratedSources)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());
            File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,generate,assemblyIdentity=output.Assembly.Identity.ToString(),exports=exports.Select(a=>new{type=a.ConstructorArguments[0].Value!.ToString(),fingerprint=a.ConstructorArguments[1].Value}),warnings=0,errors=0}));
        }
        return bytes.ToArray();
    }
    private static CatalogFixture CatalogRuntimeFixture(bool async,bool friend)=>CatalogFixtures.GetOrAdd((async,friend),k=>new(()=>{
        var valueRequest=k.Async==k.Friend;var result=k.Friend?(k.Async?"nullable":"array"):(k.Async?"array":"large");var wrapper=k.Async&&!k.Friend;
        var source=ManagedShapeSource(k.Async,valueRequest,result,wrapper);var split=source.IndexOf("[ZendiatorComposition(\"shapes\")]",StringComparison.Ordinal);var ns=source.IndexOf("namespace App{",StringComparison.Ordinal);
        var consumerName="CatalogConsumer_"+Guid.NewGuid().ToString("N");var producer=source[..split]+"public sealed class CatalogMarker;}";
        if(k.Friend){
            producer=producer.Replace("public sealed class Hook:","internal sealed class Hook:").Replace("public sealed class Handler:","internal sealed class Handler:").Replace("public sealed class Factory:","internal sealed class Factory:");
            producer=producer.Replace("namespace App{","[assembly:global::System.Runtime.CompilerServices.InternalsVisibleTo(\""+consumerName+"\")]\nnamespace App{");
            producer+="namespace App{public sealed record ExportedPing:ISyncRequest<int>;[ZendiatorHandler]public sealed class Exported:ISyncRequestHandler<ExportedPing,int>{public int Handle(ExportedPing r,CancellationToken t)=>1;}}";
        }else{
            producer+="namespace App{public sealed class UnmarkedNoise:"+(k.Async?"IRequestHandler":"ISyncRequestHandler")+"<Ping,"+(result=="array"?"int[]":"Reply")+">{public "+(k.Async?"ValueTask<int[]> HandleAsync":"Reply Handle")+"(Ping r,CancellationToken t)=>"+(k.Async?"new(new[]{-1})":"new(-1,0,0,0,0,0,0,0)")+";}}";
        }
        var name=k.Async+"-"+k.Friend;var data=CatalogProducerBytes(producer,name,"CatalogProducer_"+Guid.NewGuid().ToString("N"),true);var reference=MetadataReference.CreateFromImage(data);
        using var producerStream=new MemoryStream(data);var producerAssembly=AssemblyLoadContext.Default.LoadFromStream(producerStream);
        var consumer=source[..ns]+"namespace App{"+source[split..];consumer=consumer.Replace("c=>{c.Handlers","c=>{c.UseCatalogFrom<CatalogMarker>();c.Handlers");
        if(k.Friend)consumer=consumer.Replace("c.Handlers","c.AddHandler<Handler>();c.Handlers");
        var compilation=SyncCompilation(consumer,consumerName).AddReferences(reference);var assembly=(IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        var errors=new List<string>();var imported=GeneratedHandlerCatalog.Read(compilation,assembly,CancellationToken.None,errors.Add);Assert.Empty(errors);Assert.Single(imported);Assert.Equal(k.Friend?"Exported":"Handler",imported[0].Symbol.Name);
        var exported=assembly.GetAttributes().Single(a=>a.AttributeClass?.ToDisplayString()=="Zendiator.ZendiatorHandlerExportAttribute");var exportedType=(INamedTypeSymbol)exported.ConstructorArguments[0].Value!;
        Assert.Equal(CatalogV1Reference.Fingerprint(compilation,exportedType),exported.ConstructorArguments[1].Value);
        var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(compilation,out var output,out var diagnostics);Assert.Empty(diagnostics);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));var generated=driver.GetRunResult().Results.Single().GeneratedSources;Assert.Equal(2,generated.Length);
        var body=generated.Single(s=>!s.HintName.Contains("Interceptor",StringComparison.Ordinal)).SourceText.ToString();
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))){var p=Path.Combine(CatalogArtifactRoot,"generated-artifacts","catalog-"+name);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),consumer);foreach(var s in generated)File.WriteAllText(Path.Combine(p,s.HintName),s.SourceText.ToString());File.WriteAllBytes(Path.Combine(p,"generated.dll"),bytes.ToArray());File.WriteAllText(Path.Combine(p,"compile.json"),JsonSerializer.Serialize(new{emitted.Success,productionEnabled=true,candidateOnly=false,warnings=0,errors=0,generatorErrors=0,catalogExports=imported.Length,friend=k.Friend,publicCatalogExport=true,sourceMetadataFingerprintEqual=true}));}
        bytes.Position=0;var a=AssemblyLoadContext.Default.LoadFromStream(bytes);var bridge=a.GetType("App.Bridge")!;
        return new(new(a,a.GetType("App.Generated.Reads.Zendiator")!,producerAssembly.GetType("App.Ping")!,bridge.GetMethod("Register")!,bridge.GetMethod("Dispatch")!,a.GetType("App.Oracle")!.GetMethod("Send")!,result=="array"?typeof(int[]):result=="nullable"?typeof(int?):producerAssembly.GetType("App.Reply")!,body),reference,producer,k.Async,valueRequest,result,wrapper,consumerName);
    })).Value;
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task Catalog_v1_producer_consumer_and_friend_internal_API_match_reference(bool async,bool friend)
    {var f=CatalogRuntimeFixture(async,friend);foreach(var generated in new[]{false,true})await RunManagedShape(f.Runtime,f.Async,f.ValueRequest,f.Result,f.Wrapper,generated,false,false);}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Catalog_v1_pending_body_and_before_cancel_preserve_owned_cleanup(bool cancel)
    {var f=CatalogRuntimeFixture(true,false);foreach(var generated in new[]{false,true})await RunManagedShape(f.Runtime,f.Async,f.ValueRequest,f.Result,f.Wrapper,generated,!cancel,cancel);}
    private static string CatalogConsumerRegistration(bool duplicate=false)=>"using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;namespace Consumer{[ZendiatorComposition(\"catalog\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{c.UseCatalogFrom<App.CatalogMarker>();"+(duplicate?"c.UseCatalogFrom<App.Ping>();":"")+"c.Handlers<App.Ping>(DispatchFamily.SendAsync,HandlerEntry.Session<App.Handler,App.Factory>());c.Pipeline<App.Ping>(DispatchFamily.SendAsync,Stage.Hook<App.Hook>());});}}";
}
