using System.Text.Json;
using Microsoft.CodeAnalysis;
using Zendiator.SourceGenerator;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public void Catalog_v1_nested_generic_unicode_constraint_fingerprint_survives_real_metadata()
    {
        var source = "using System.Threading;using System.Threading.Tasks;using Zendiator;namespace 公開{public sealed record Box<T,U>(T Value):IRequest<T> where T:class where U:class;public class Outer<U> where U:class{[ZendiatorHandler]public sealed class Inner<T>:IRequestHandler<Box<T,U>,T> where T:class,new(){public ValueTask<T> HandleAsync(Box<T,U> r,CancellationToken t)=>new(r.Value);}}}";
        var bytes = CatalogProducerBytes(source, "generic-unicode", "CatalogGeneric_" + Guid.NewGuid().ToString("N"), true); var reference = MetadataReference.CreateFromImage(bytes); var compilation = SyncCompilation("public sealed class Consumer;", "CatalogGenericConsumer").AddReferences(reference); var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(reference)!;
        var errors = new List<string>(); var imported = GeneratedHandlerCatalog.Read(compilation, assembly, CancellationToken.None, errors.Add); Assert.Empty(errors); var type = Assert.Single(imported).Symbol;
        var export = assembly.GetAttributes().Single(a => a.AttributeClass?.ToDisplayString() == "Zendiator.ZendiatorHandlerExportAttribute"); Assert.Equal(CatalogV1Reference.Fingerprint(compilation, type), export.ConstructorArguments[1].Value);
        CatalogEvidence("catalog-schema", "generic-unicode", new { type = type.ToDisplayString(), sourceMetadataReferenceMatched = true, runtimeOpenRouteExecuted = false });
    }
    [Fact]
    public void Catalog_v1_future_minor_accepts_known_schema_and_generates_consumer()
    {
        var f = CatalogRuntimeFixture(true, false); var name = "CatalogFuture_" + Guid.NewGuid().ToString("N"); var initial = SyncCompilation(f.ProducerSource, name);
        var header = "[assembly:global::Zendiator.ZendiatorGeneratedCatalogAttribute(1,17," + JsonSerializer.Serialize(initial.Assembly.Identity.ToString()) + "," + JsonSerializer.Serialize(CatalogSignatureV1.Algorithm) + ")]";
        var export = "[assembly:global::Zendiator.ZendiatorHandlerExportAttribute(typeof(global::App.Handler),\"" + CatalogV1Reference.Fingerprint(initial, initial.GetTypeByMetadataName("App.Handler")!) + "\")]";
        var source = f.ProducerSource.Insert(f.ProducerSource.IndexOf("namespace App{", StringComparison.Ordinal), header + export + "\n");
        var data = CatalogProducerBytes(source, "future-minor", name, false); var reference = MetadataReference.CreateFromImage(data); var consumer = CatalogConsumerRegistration();
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(consumer, "CatalogFutureConsumer_" + Guid.NewGuid().ToString("N")).AddReferences(reference), out var output, out var diagnostics); Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); var sources = driver.GetRunResult().Results.Single().GeneratedSources; AssertCanonicalOutputRoles(sources);
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))) { var p = Path.Combine(CatalogArtifactRoot, "generated-artifacts", "catalog-future-minor"); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "input.cs"), consumer); foreach (var s in sources) File.WriteAllText(Path.Combine(p, s.HintName), s.SourceText.ToString()); File.WriteAllBytes(Path.Combine(p, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(p, "compile.json"), JsonSerializer.Serialize(new { emitted.Success, productionEnabled = true, candidateOnly = false, warnings = 0, errors = 0, generatorErrors = 0, major = 1, minor = 17, runtimeExecuted = false })); }
        CatalogEvidence("catalog-schema", "future-minor", new { major = 1, minor = 17, acceptedKnownSchema = true });
    }
}
