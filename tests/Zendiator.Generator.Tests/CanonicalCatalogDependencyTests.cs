using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record CatalogDependencyFixture(byte[] DependencyBytes,byte[] ProducerBytes,MetadataReference Dependency,MetadataReference Producer,string Source);
    private static byte[] DependencyEmit(Compilation compilation,bool generate,string label)
    {
        GeneratorDriver? driver=null;Compilation output=compilation;
        if(generate){driver=SyncDriver().RunGeneratorsAndUpdateCompilation(compilation,out output,out var diagnostics);Assert.Empty(diagnostics);}
        Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));using var bytes=new MemoryStream();var emitted=output.Emit(bytes);Assert.True(emitted.Success,string.Join("\n",emitted.Diagnostics));
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))){var p=Path.Combine(CatalogArtifactRoot,"dependency-producers",label);Directory.CreateDirectory(p);File.WriteAllText(Path.Combine(p,"input.cs"),string.Join("\n",compilation.SyntaxTrees.Select(t=>t.GetText().ToString())));File.WriteAllBytes(Path.Combine(p,"producer.dll"),bytes.ToArray());if(driver!=null)foreach(var source in driver.GetRunResult().Results.Single().GeneratedSources)File.WriteAllText(Path.Combine(p,source.HintName),source.SourceText.ToString());}
        return bytes.ToArray();
    }
    private static CatalogDependencyFixture DependencyFixture(string label,bool catalog=true)
    {
        var suffix=Guid.NewGuid().ToString("N");var dependencySource="using Zendiator;namespace Payloads{public sealed record Request<T>(T Value):ISyncRequest<int> where T:class;}";
        var dependency=DependencyEmit(SyncCompilation(dependencySource,"Payloads_"+suffix),false,label+"-payloads");var dependencyRef=MetadataReference.CreateFromImage(dependency);
        var producerSource="using System;using System.Threading;using Zendiator;using Microsoft.Extensions.DependencyInjection;namespace Library{public sealed class Marker;"+(catalog?"[ZendiatorHandler]":"")+"public sealed class Handler<T>:ISyncRequestHandler<Payloads.Request<T>,int> where T:class{public int Handle(Payloads.Request<T> r,CancellationToken t)=>r.Value.ToString()!.Length;}public static class RuntimeRegistration{public static int Calls;public static IServiceCollection Register(IServiceCollection s){Calls++;throw new InvalidOperationException(\"Runtime registration must not be used for discovery.\");}}}";
        var producer=DependencyEmit(SyncCompilation(producerSource,"Handlers_"+suffix).AddReferences(dependencyRef),catalog,label+"-handlers");var producerRef=MetadataReference.CreateFromImage(producer);
        return new(dependency,producer,dependencyRef,producerRef,producerSource);
    }
    private static string DependencyConsumer(bool fallback=false,bool route=false)=>"using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;namespace Consumer{[ZendiatorComposition(\"dependency\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{"+(fallback?"c.AddHandler<Library.Handler<string>>();":"c.UseCatalogFrom<Library.Marker>();")+(route?"c.Handlers<Payloads.Request<string>>(DispatchFamily.SendSync,typeof(Library.Handler<string>));":"")+"});}}";
    [Fact]
    public void Catalog_dependency_open_export_and_explicit_closed_route_generate_without_runtime_registration()
    {
        var f=DependencyFixture("open-route");var compilation=SyncCompilation("public sealed class Inspection;","DependencyInspection").AddReferences(f.Dependency,f.Producer);var producer=(IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(f.Producer)!;var errors=new List<string>();var imported=GeneratedHandlerCatalog.Read(compilation,producer,CancellationToken.None,errors.Add);Assert.Empty(errors);var handler=Assert.Single(imported).Symbol;Assert.Equal(1,handler.Arity);Assert.Equal(TypeKind.TypeParameter,handler.TypeArguments[0].TypeKind);
        using var d=new MemoryStream(f.DependencyBytes);using var p=new MemoryStream(f.ProducerBytes);var dependencyAssembly=AssemblyLoadContext.Default.LoadFromStream(d);var producerAssembly=AssemblyLoadContext.Default.LoadFromStream(p);
        var assembly=IdentityConsumer(DependencyConsumer(route:true),[f.Dependency,f.Producer],"dependency-open-route",out _);IServiceCollection services=new ServiceCollection();assembly.GetType("Consumer.Bridge")!.GetMethod("Register")!.Invoke(null,[services]);using var provider=services.BuildServiceProvider();var mediator=provider.GetRequiredService(assembly.GetType("Consumer.Generated.Reads.Zendiator")!);var requestType=dependencyAssembly.GetType("Payloads.Request`1")!.MakeGenericType(typeof(string));var request=Activator.CreateInstance(requestType,["abcde"])!;Assert.Equal(5,mediator.GetType().GetMethod("Send")!.Invoke(mediator,[request,CancellationToken.None]));Assert.Single(services,s=>s.ServiceType==producerAssembly.GetType("Library.Handler`1")!.MakeGenericType(typeof(string)));Assert.Equal(0,producerAssembly.GetType("Library.RuntimeRegistration")!.GetField("Calls")!.GetValue(null));
        CatalogEvidence("dependency-observations","open-route",new{exportIsOpenDefinition=true,explicitClosedRoute=true,result=5,runtimeRegistrationCalls=0,handlerDescriptors=1});
    }
    [Fact]
    public void Catalog_dependency_missing_request_metadata_reports_actionable_import_error()
    {
        var f=DependencyFixture("missing");var source=DependencyConsumer();var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"MissingDependencyConsumer").AddReferences(f.Producer),out var output,out var diagnostics);Assert.Null(driver.GetRunResult().Results.Single().Exception);var d=Assert.Single(diagnostics);Assert.Contains("CatalogDependencyUnavailable",d.GetMessage());Assert.Contains("Reference the producer's required assemblies",d.GetMessage());Assert.Contains("UseCatalogFrom",source.Substring(d.Location.SourceSpan.Start,d.Location.SourceSpan.Length));AssertNoCanonicalOutput(driver.GetRunResult().Results.Single().GeneratedSources);
        CatalogEvidence("dependency-observations","missing",new{actionable=true,diagnostic=d.GetMessage(),importLocation=d.Location.SourceSpan.ToString(),partialBackend=false,compiler=output.GetDiagnostics().Select(x=>x.ToString())});
    }
    [Fact]
    public void Catalog_dependency_restored_reference_invalidates_error_and_recovers_membership()
    {
        var f=DependencyFixture("restore");var source=DependencyConsumer().Replace("namespace Consumer{","namespace Consumer{public sealed record LocalPing:ISyncRequest<int>;[ZendiatorHandler]public sealed class LocalHandler:ISyncRequestHandler<LocalPing,int>{public int Handle(LocalPing r,System.Threading.CancellationToken t)=>7;}").Replace("c.UseCatalogFrom<Library.Marker>();","c.UseCatalogFrom<Library.Marker>();c.Handlers<LocalPing>(DispatchFamily.SendSync,typeof(LocalHandler));");var bad=SyncCompilation(source,"RestoredDependencyConsumer").AddReferences(f.Producer);var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(bad,out _,out var missing);Assert.Contains("CatalogDependencyUnavailable",Assert.Single(missing).GetMessage());
        var good=bad.AddReferences(f.Dependency);driver=driver.RunGeneratorsAndUpdateCompilation(good,out var output,out var recovered);Assert.Empty(recovered);Assert.Null(driver.GetRunResult().Results.Single().Exception);Assert.Empty(output.GetDiagnostics().Where(d=>d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));var producer=(IAssemblySymbol)good.GetAssemblyOrModuleSymbol(f.Producer)!;var errors=new List<string>();Assert.Single(GeneratedHandlerCatalog.Read(good,producer,CancellationToken.None,errors.Add));Assert.Empty(errors);
        var model=(CompositionSelectionCatalog)Assert.Single(driver.GetRunResult().Results.Single().TrackedSteps["CompositionSelectionModel"].SelectMany(s=>s.Outputs)).Value;Assert.Equal(2,Assert.Single(model.Compositions).Handlers.Count);Assert.Contains(model.Compositions[0].Handlers,h=>h.Definition.Name.StartsWith("global::Library.Handler<",StringComparison.Ordinal));AssertCanonicalOutputRoles(driver.GetRunResult().Results.Single().GeneratedSources);
        CatalogEvidence("dependency-observations","restore",new{missingDiagnosticRemoved=true,externalMembershipRestored=1,localMembership=1,generatorException=false,canonicalBackendRestored=true});
    }
    [Fact]
    public void Catalog_dependency_runtime_only_registration_requires_catalog_or_explicit_fallback()
    {
        var f=DependencyFixture("runtime-only",catalog:false);var source=DependencyConsumer();var driver=SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source,"RuntimeOnlyConsumer").AddReferences(f.Dependency,f.Producer),out _,out var diagnostics);Assert.Contains("MissingCatalog",Assert.Single(diagnostics).GetMessage());AssertNoCanonicalOutput(driver.GetRunResult().Results.Single().GeneratedSources);
        using var d=new MemoryStream(f.DependencyBytes);using var p=new MemoryStream(f.ProducerBytes);var dependencyAssembly=AssemblyLoadContext.Default.LoadFromStream(d);var producerAssembly=AssemblyLoadContext.Default.LoadFromStream(p);var assembly=IdentityConsumer(DependencyConsumer(fallback:true,route:true),[f.Dependency,f.Producer],"dependency-runtime-fallback",out _);IServiceCollection services=new ServiceCollection();assembly.GetType("Consumer.Bridge")!.GetMethod("Register")!.Invoke(null,[services]);using var provider=services.BuildServiceProvider();var mediator=provider.GetRequiredService(assembly.GetType("Consumer.Generated.Reads.Zendiator")!);var request=Activator.CreateInstance(dependencyAssembly.GetType("Payloads.Request`1")!.MakeGenericType(typeof(string)),["xyz"])!;Assert.Equal(3,mediator.GetType().GetMethod("Send")!.Invoke(mediator,[request,CancellationToken.None]));Assert.Equal(0,producerAssembly.GetType("Library.RuntimeRegistration")!.GetField("Calls")!.GetValue(null));
        CatalogEvidence("dependency-observations","runtime-only",new{runtimeMethodNotDiscovery=true,explicitFallback=true,result=3,runtimeRegistrationCalls=0});
    }
}
