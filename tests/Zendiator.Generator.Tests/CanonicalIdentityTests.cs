using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;
namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static byte[] IdentityProducer(string name, int value)
    {
        var source = "using System.Threading;using Zendiator;namespace Twins{public sealed class Marker;public sealed record Ping(int Value):ISyncRequest<int>;[ZendiatorHandler]public sealed class Handler:ISyncRequestHandler<Ping,int>{public int Handle(Ping p,CancellationToken t)=>p.Value+" + value + ";}}";
        return CatalogProducerBytes(source, "identity-" + name, name, true);
    }
    private static byte[] IdentityForwarder(string name, MetadataReference target)
    {
        var source = "[assembly:System.Runtime.CompilerServices.TypeForwardedTo(typeof(Twins.Marker))][assembly:System.Runtime.CompilerServices.TypeForwardedTo(typeof(Twins.Ping))][assembly:System.Runtime.CompilerServices.TypeForwardedTo(typeof(Twins.Handler))]";
        var compilation = SyncCompilation(source, name).AddReferences(target); using var bytes = new MemoryStream(); var emitted = compilation.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))) { var p = Path.Combine(CatalogArtifactRoot, "identity-producers", name); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "input.cs"), source); File.WriteAllBytes(Path.Combine(p, "producer.dll"), bytes.ToArray()); }
        return bytes.ToArray();
    }
    private static Assembly IdentityConsumer(string source, IEnumerable<MetadataReference> references, string fixture, out string body, params (string Request, string Handler)[] identities)
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "IdentityConsumer_" + Guid.NewGuid().ToString("N")).AddReferences(references), out var output, out var diagnostics);
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        if (identities.Length == 0) body = CanonicalDispatchSource(generated);
        else
        {
            AssertCanonicalOutputRoles(generated, routes: identities.Length);
            foreach (var identity in identities)
            {
                Assert.Single(generated, s => IsCanonicalRoute(s) && s.SourceText.ToString().Contains("Send(" + identity.Request + " request", StringComparison.Ordinal));
                Assert.Contains(identity.Handler, CanonicalCompositionSource(generated));
            }
            body = string.Concat(generated.Where(IsCanonicalRoute).Select(s => s.SourceText.ToString())) + CanonicalCompositionSource(generated);
        }
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"))) { var p = Path.Combine(CatalogArtifactRoot, "generated-artifacts", fixture); Directory.CreateDirectory(p); File.WriteAllText(Path.Combine(p, "input.cs"), source); foreach (var s in generated) File.WriteAllText(Path.Combine(p, s.HintName), s.SourceText.ToString()); File.WriteAllBytes(Path.Combine(p, "generated.dll"), bytes.ToArray()); File.WriteAllText(Path.Combine(p, "compile.json"), JsonSerializer.Serialize(new { emitted.Success, productionEnabled = true, candidateOnly = false, warnings = 0, errors = 0, generatorErrors = 0 })); }
        bytes.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(bytes);
    }
    private static string IdentityRegistration(string prefix, string config, string dispatch = "") => prefix + "using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;namespace Consumer{[ZendiatorComposition(\"identity\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>{" + config + "});" + dispatch + "}}";
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identity_alias_same_metadata_names_keep_assembly_routes_and_keyword_paths_distinct(bool keyword)
    { RunIdentityAlias(keyword, false); }
    [Fact]
    public void Identity_alias_shared_group_collision_chooses_unique_assembly_paths()
    { RunIdentityAlias(false, true); }
    private static void RunIdentityAlias(bool keyword, bool shared)
    {
        var suffix = Guid.NewGuid().ToString("N"); var a = IdentityProducer("Left_" + suffix, 1); var b = IdentityProducer("Right_" + suffix, 2); var alias = shared ? "z" : keyword ? "@class" : "left"; var rightAlias = shared ? "b" : "right";
        var properties = MetadataReferenceProperties.Assembly.WithAliases(shared ? ImmutableArray.Create("a", "z") : ImmutableArray.Create(keyword ? "class" : "left"));
        var refs = new[] { MetadataReference.CreateFromImage(a, properties), MetadataReference.CreateFromImage(b, MetadataReferenceProperties.Assembly.WithAliases(shared ? ImmutableArray.Create("a", "b") : ImmutableArray.Create("right"))) };
        using var aBytes = new MemoryStream(a); using var bBytes = new MemoryStream(b); var aa = AssemblyLoadContext.Default.LoadFromStream(aBytes); var ba = AssemblyLoadContext.Default.LoadFromStream(bBytes);
        var config = "c.UseCatalogFrom<" + alias + "::Twins.Marker>();c.UseCatalogFrom<" + rightAlias + "::Twins.Marker>();c.Handlers<" + alias + "::Twins.Ping>(DispatchFamily.SendSync,typeof(" + alias + "::Twins.Handler));c.Handlers<" + rightAlias + "::Twins.Ping>(DispatchFamily.SendSync,typeof(" + rightAlias + "::Twins.Handler));";
        var dispatch = "public static int SendLeft(global::Consumer.Generated.Reads.Zendiator mediator," + alias + "::Twins.Ping request)=>mediator.Send(request);" +
            "public static int SendRight(global::Consumer.Generated.Reads.IZendiator mediator," + rightAlias + "::Twins.Ping request)=>mediator.Send(request);";
        var source = IdentityRegistration("extern alias " + alias + ";extern alias " + rightAlias + ";", config, dispatch);
        var compilation = SyncCompilation(source, "IdentityShape").AddReferences(refs); var left = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(refs[0])!; var right = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(refs[1])!;
        Assert.NotEqual(MarkedHandlerDiscovery.RegistrationIdentity(left.GetTypeByMetadataName("Twins.Ping")!, compilation), MarkedHandlerDiscovery.RegistrationIdentity(right.GetTypeByMetadataName("Twins.Ping")!, compilation));
        var assembly = IdentityConsumer(source, refs, "identity-alias-" + (shared ? "shared" : keyword.ToString()), out var body, (alias + "::Twins.Ping", alias + "::Twins.Handler"), (rightAlias + "::Twins.Ping", rightAlias + "::Twins.Handler")); Assert.Contains("extern alias " + alias + ";", body); Assert.Contains(alias + "::Twins.Ping", body); Assert.Contains(rightAlias + "::Twins.Ping", body); if (shared) { Assert.DoesNotContain("a::Twins.", body); Assert.Contains("z::Twins.Handler", body); Assert.Contains("b::Twins.Handler", body); }
        IServiceCollection services = new ServiceCollection(); assembly.GetType("Consumer.Bridge")!.GetMethod("Register")!.Invoke(null, [services]); using var provider = services.BuildServiceProvider(); var mediator = provider.GetRequiredService(assembly.GetType("Consumer.Generated.Reads.IZendiator")!);
        foreach (var pair in new[] { (aa, 11, "SendLeft"), (ba, 12, "SendRight") }) { var request = Activator.CreateInstance(pair.Item1.GetType("Twins.Ping")!, [10])!; Assert.Equal(pair.Item2, mediator.GetType().GetMethod("Send", [request.GetType(), typeof(CancellationToken)])!.Invoke(mediator, [request, CancellationToken.None])); Assert.Equal(pair.Item2, assembly.GetType("Consumer.Bridge")!.GetMethod(pair.Item3)!.Invoke(null, [mediator, request])); Assert.Single(services, d => d.ServiceType == pair.Item1.GetType("Twins.Handler")); }
        CatalogEvidence("identity-observations", "aliases-" + (shared ? "shared" : keyword.ToString()), new { keyword, shared, distinctAssemblyIdentity = true, directMediatorCalls = true, reflectionCalls = true, routes = 2, descriptors = 2, results = new[] { 11, 12 }, selectedAliases = new[] { alias, rightAlias } });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identity_alias_generic_contract_array_hook_wrapper_factory_match_existing_reference(bool wrapper)
    {
        var source = ManagedShapeSource(true, true, "array", wrapper); var split = source.IndexOf("[ZendiatorComposition(\"shapes\")]", StringComparison.Ordinal); var ns = source.IndexOf("namespace App{", StringComparison.Ordinal);
        var producerSource = source[..split] + "}"; var producerBytes = CompileExplicitMetadataProducer(producerSource, "identity-async-" + wrapper); var reference = MetadataReference.CreateFromImage(producerBytes, MetadataReferenceProperties.Assembly.WithAliases(ImmutableArray.Create("callbacks")));
        using var producerStream = new MemoryStream(producerBytes); var producer = AssemblyLoadContext.Default.LoadFromStream(producerStream);
        var prefix = "extern alias callbacks;using Ping=callbacks::App.Ping;using Handler=callbacks::App.Handler;using Hook=callbacks::App.Hook;using Factory=callbacks::App.Factory;" + (wrapper ? "using W=callbacks::App.W;" : "");
        var consumer = prefix + source[..ns] + "namespace App{" + source[split..]; var assembly = IdentityConsumer(consumer, [reference], "identity-async-" + wrapper, out var body);
        Assert.Contains("callbacks::App.Ping", body); Assert.Contains("global::Zendiator.IRequestHandler<callbacks::App.Ping, int[]>", body); Assert.Contains("callbacks::App.Factory", body); Assert.Contains("callbacks::App.Hook", body); if (wrapper) Assert.Contains("callbacks::App.W", body);
        var bridge = assembly.GetType("App.Bridge")!; var f = new ManagedShapeFixture(assembly, assembly.GetType("App.Generated.Reads.Zendiator")!, producer.GetType("App.Ping")!, bridge.GetMethod("Register")!, bridge.GetMethod("Dispatch")!, assembly.GetType("App.Oracle")!.GetMethod("Send")!, typeof(int[]), body);
        foreach (var generated in new[] { false, true }) await RunManagedShape(f, true, true, "array", wrapper, generated, false, false);
        CatalogEvidence("identity-observations", "async-" + wrapper, new { wrapper, typedReferenceMatched = true, ownedCleanup = true, genericContractArgumentPath = true, arrayResult = true });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identity_forwarded_marker_and_diamond_paths_resolve_one_definition(bool diamond)
    {
        var suffix = Guid.NewGuid().ToString("N"); var bytes = IdentityProducer("Leaf_" + suffix, 7); var target = MetadataReference.CreateFromImage(bytes);
        var f0 = MetadataReference.CreateFromImage(IdentityForwarder("F0_" + suffix, target), MetadataReferenceProperties.Assembly.WithAliases(ImmutableArray.Create("f0")));
        var f1 = diamond ? MetadataReference.CreateFromImage(IdentityForwarder("F1_" + suffix, target), MetadataReferenceProperties.Assembly.WithAliases(ImmutableArray.Create("f1"))) : null;
        // An alias namespace is built from definitions in its reference group. Include the
        // defining assembly in the same group; the PE facades still prove forwarded identity.
        var visibleTarget = target.WithAliases(diamond ? ImmutableArray.Create("global", "f0", "f1") : ImmutableArray.Create("global", "f0"));
        var refs = diamond ? new[] { visibleTarget, f0, f1! } : new[] { visibleTarget, f0 }; using var stream = new MemoryStream(bytes); var leaf = AssemblyLoadContext.Default.LoadFromStream(stream);
        var config = "c.UseCatalogFrom<f0::Twins.Marker>();" + (diamond ? "c.AddHandler<f1::Twins.Handler>();" : "") + "c.Handlers<f0::Twins.Ping>(DispatchFamily.SendSync,typeof(" + (diamond ? "f1" : "f0") + "::Twins.Handler));";
        var source = IdentityRegistration("extern alias f0;" + (diamond ? "extern alias f1;" : ""), config); var compilation = SyncCompilation(source, "ForwardIdentity").AddReferences(refs); var endpoint = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(visibleTarget)!; var forwarded = ((IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(f0)!).ResolveForwardedType("Twins.Handler")!; Assert.True(SymbolEqualityComparer.Default.Equals(endpoint.GetTypeByMetadataName("Twins.Handler"), forwarded));
        var assembly = IdentityConsumer(source, refs, "identity-forward-" + diamond, out _); IServiceCollection services = new ServiceCollection(); assembly.GetType("Consumer.Bridge")!.GetMethod("Register")!.Invoke(null, [services]); using var provider = services.BuildServiceProvider(); var mediator = provider.GetRequiredService(assembly.GetType("Consumer.Generated.Reads.IZendiator")!); var request = Activator.CreateInstance(leaf.GetType("Twins.Ping")!, [10])!; Assert.Equal(17, mediator.GetType().GetMethod("Send")!.Invoke(mediator, [request, CancellationToken.None])); Assert.Single(services, d => d.ServiceType == leaf.GetType("Twins.Handler"));
        CatalogEvidence("identity-observations", "forward-" + diamond, new { diamond, definingAssemblyIdentityPreserved = true, catalogMembershipDeduplicated = true, descriptors = 1, result = 17 });
    }
    [Fact]
    public void Identity_diamond_explicit_catalog_duplicate_retains_both_locations()
    {
        var suffix = Guid.NewGuid().ToString("N"); var target = MetadataReference.CreateFromImage(IdentityProducer("DupLeaf_" + suffix, 1)); var f0 = MetadataReference.CreateFromImage(IdentityForwarder("DupF0_" + suffix, target), MetadataReferenceProperties.Assembly.WithAliases(ImmutableArray.Create("f0"))); var f1 = MetadataReference.CreateFromImage(IdentityForwarder("DupF1_" + suffix, target), MetadataReferenceProperties.Assembly.WithAliases(ImmutableArray.Create("f1")));
        target = target.WithAliases(ImmutableArray.Create("global", "f0", "f1"));
        var source = IdentityRegistration("extern alias f0;extern alias f1;", "c.UseCatalogFrom<f0::Twins.Marker>();c.UseCatalogFrom<f1::Twins.Marker>();c.Handlers<f0::Twins.Ping>(DispatchFamily.SendSync,typeof(f1::Twins.Handler));");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "DiamondDuplicate").AddReferences(target, f0, f1), out var output, out var diagnostics); var d = Assert.Single(diagnostics); Assert.Contains("DuplicateCatalogSelection", d.GetMessage()); Assert.Single(d.AdditionalLocations); Assert.Contains("f1::Twins.Marker", source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)); Assert.Contains("f0::Twins.Marker", source.Substring(d.AdditionalLocations[0].SourceSpan.Start, d.AdditionalLocations[0].SourceSpan.Length)); Assert.Empty(output.GetDiagnostics().Where(x => x.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.Contains("Canonical", StringComparison.Ordinal)); CatalogEvidence("identity-boundaries", "diamond-duplicate", new { bothLocations = true, noPartialGeneration = true });
    }
    [Fact]
    public void Identity_forwarder_retargeted_contract_rejects_old_catalog_fingerprint()
    {
        var name = "RetargetLeaf_" + Guid.NewGuid().ToString("N"); var old = MetadataReference.CreateFromImage(IdentityProducer(name, 1)); var original = SyncCompilation("", "RetargetInspect").AddReferences(old); var oldAssembly = (IAssemblySymbol)original.GetAssemblyOrModuleSymbol(old)!;
        var fingerprint = (string)oldAssembly.GetAttributes().Single(a => a.AttributeClass?.ToDisplayString() == "Zendiator.ZendiatorHandlerExportAttribute").ConstructorArguments[1].Value!;
        var forwarder = MetadataReference.CreateFromImage(IdentityForwarder("RetargetF_" + Guid.NewGuid().ToString("N"), old), MetadataReferenceProperties.Assembly.WithAliases(ImmutableArray.Create("f")));
        var replacement = "using System.Threading;using Zendiator;[assembly:ZendiatorGeneratedCatalog(1,0," + JsonSerializer.Serialize(oldAssembly.Identity.ToString()) + "," + JsonSerializer.Serialize(CatalogSignatureV1.Algorithm) + ")][assembly:ZendiatorHandlerExport(typeof(Twins.Handler)," + JsonSerializer.Serialize(fingerprint) + ")]namespace Twins{public sealed class Marker;public sealed record Ping(int Value):ISyncRequest<long>;public sealed class Handler:ISyncRequestHandler<Ping,long>{public long Handle(Ping p,CancellationToken t)=>p.Value;}}";
        var target = MetadataReference.CreateFromImage(CatalogProducerBytes(replacement, "retarget-replacement", name, false)).WithAliases(ImmutableArray.Create("global", "f")); var source = IdentityRegistration("extern alias f;", "c.UseCatalogFrom<f::Twins.Marker>();c.Handlers<f::Twins.Ping>(DispatchFamily.SendSync,typeof(f::Twins.Handler));");
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "RetargetConsumer").AddReferences(target, forwarder), out var output, out var diagnostics); var d = Assert.Single(diagnostics); Assert.Contains("StaleCatalogContract", d.GetMessage()); Assert.Contains("UseCatalogFrom", source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length)); Assert.Empty(output.GetDiagnostics().Where(x => x.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.Contains("Canonical", StringComparison.Ordinal)); CatalogEvidence("identity-boundaries", "forward-retarget", new { sameAssemblyIdentity = true, oldFingerprintRejected = true, noPartialGeneration = true });
    }
    [Theory]
    [InlineData("function-pointer")]
    [InlineData("error-contract")]
    [InlineData("null-attribute")]
    [InlineData("missing-factory")]
    [InlineData("incomplete-call")]
    public void Identity_incomplete_source_never_crashes_or_reuses_stale_backend_and_recovers(string shape)
    {
        static bool Backend(GeneratedSourceResult source) => source.HintName.Contains("Canonical", StringComparison.Ordinal) || IsCanonicalRoute(source) || IsCanonicalComposition(source);
        var source = "using System.Threading;using Zendiator;using Zendiator.DependencyInjection;using Microsoft.Extensions.DependencyInjection;namespace Broken{public sealed record Ping:ISyncRequest<int>;[ZendiatorHandler]public sealed class Handler:ISyncRequestHandler<Ping,int>{public int Handle(Ping r,CancellationToken t)=>1;}[ZendiatorComposition(\"broken\")]public sealed class Reads;public static class Bridge{public static IServiceCollection Register(IServiceCollection s)=>s.AddZendiator<Reads>(c=>c.Handlers<Ping>(DispatchFamily.SendSync,typeof(Handler)));}}";
        var invalid = shape switch { "function-pointer" => source.Replace("public sealed class Handler", "public unsafe sealed class Handler").Replace("ISyncRequestHandler<Ping,int>", "ISyncRequestHandler<Ping,delegate*<void>>").Replace("public int Handle", "public delegate*<void> Handle").Replace("=>1", "=>null"), "error-contract" => source.Replace("ISyncRequestHandler<Ping,int>", "ISyncRequestHandler<Missing,int>").Replace("Handle(Ping r", "Handle(Missing r"), "null-attribute" => source.Replace("Composition(\"broken\")", "Composition(null!)"), "missing-factory" => source.Replace("typeof(Handler)", "HandlerEntry.Session<Handler,Missing>()"), _ => source.Replace("typeof(Handler)));", "typeof(Handler)") };
        var name = "Incomplete_" + shape; var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, name), out var valid, out var good); Assert.Empty(good); var first = driver.GetRunResult().Results.Single().GeneratedSources.Where(Backend).ToDictionary(s => s.HintName, s => s.SourceText.ToString()); Assert.NotEmpty(first);
        var bad = SyncCompilation(invalid, name).WithOptions(((CSharpCompilationOptions)SyncCompilation(invalid, name).Options).WithAllowUnsafe(true)); driver = driver.RunGeneratorsAndUpdateCompilation(bad, out var broken, out var diagnostics); Assert.Null(driver.GetRunResult().Results.Single().Exception); Assert.DoesNotContain(diagnostics, d => d.Id is "CS8785" or "CS8784"); Assert.True(diagnostics.Length > 0 || broken.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error)); Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, Backend);
        driver = driver.RunGeneratorsAndUpdateCompilation(SyncCompilation(source, name), out var recovered, out var recoveredDiagnostics); Assert.Empty(recoveredDiagnostics); Assert.Null(driver.GetRunResult().Results.Single().Exception); Assert.Empty(recovered.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); var final = driver.GetRunResult().Results.Single().GeneratedSources.Where(Backend).ToDictionary(s => s.HintName, s => s.SourceText.ToString()); Assert.Equal(first.Keys.OrderBy(x => x), final.Keys.OrderBy(x => x)); foreach (var pair in first) Assert.Equal(pair.Value, final[pair.Key]);
        CatalogEvidence("identity-incomplete", shape, new { shape, invalidSource = invalid, noGeneratorException = true, noStaleBackend = true, recoveredBodyEqual = true, compiler = broken.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => new { d.Id, message = d.GetMessage() }), generator = diagnostics.Select(d => new { d.Id, message = d.GetMessage() }) });
    }
    [Fact]
    public void Identity_preserves_accepted_class_backend_generation_without_runtime_replay()
    { Multi_route_single_backend_generation_preserves_execution_body_with_standard_provider_contract(); }
}
