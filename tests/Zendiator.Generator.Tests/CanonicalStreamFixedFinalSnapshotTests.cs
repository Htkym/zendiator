using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public void Stream_open_owned_map_preserves_constructed_final_in_the_registration_snapshot()
    {
        var source = TypedIntegrationSource("open-session")
            .Replace("typeof(OpenFinal<>)", "typeof(OpenFinal<OpenPayload>)")
            .Replace("IStreamItemMap<R,T,OpenFinal<T>>", "IStreamItemMap<R,T,OpenFinal<OpenPayload>>")
            .Replace("public OpenFinal<T> Map(R r,T item,CancellationToken c)=>new(p.MapOpen(r!,item,c));",
                "public OpenFinal<OpenPayload> Map(R r,T item,CancellationToken c)=>new(new OpenPayload());") + """
            public static class FixedFinalEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<OpenFinal<OpenPayload>> Stream(System.IServiceProvider provider,OpenRequest<OpenPayload> request)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(provider).StreamAsync(request);
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "FixedFinal_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_FIXED_FINAL_SNAPSHOT_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(root))
        {
            Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "input.cs"), source); File.WriteAllBytes(Path.Combine(root, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(root, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(root, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                emitted.Success,
                composition = Selection(driver),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        var route = Assert.Single(Selection(driver).Pipelines); var registration = Assert.IsType<BoundStreamOpenRegistration>(route.StreamOpenRegistration);
        Assert.Equal("global::App.OpenFinal<global::App.OpenPayload>", registration.FinalDefinitionName);
        Assert.Equal("global::App.OpenFinal<global::App.OpenPayload>", route.StreamItemFlow!.Final.Name);
        var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("expected.StreamPipeline(typeof(global::App.OpenRequest<>), typeof(global::App.OpenFinal<global::App.OpenPayload>),", facade);
        Assert.Contains("IAsyncEnumerable<global::App.OpenFinal<global::App.OpenPayload>> StreamAsync<__ZendiatorType0>", facade);
        Assert.DoesNotContain("typeof(global::App.OpenFinal<>)", facade);
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var register = assembly.GetType("App.FixedFinalEntry")!.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var services = new ServiceCollection(); register(services);
        Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == assembly.GetType("App.OpenHandlerFactory`1")).Lifetime);
        Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == assembly.GetType("App.OpenMapFactory`2")).Lifetime);
        if (!string.IsNullOrEmpty(root)) File.WriteAllText(Path.Combine(root, "snapshot.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            declaredFinal = registration.FinalDefinitionName,
            boundFinal = route.StreamItemFlow.Final.Name,
            snapshotFingerprintMatched = true,
            registrationCalls = 1,
            services = services.Select(d => new { type = d.ServiceType.ToString(), lifetime = d.Lifetime.ToString() }),
            providerBuilds = 0,
            factoryAcquisitions = 0,
            handlerStarts = 0,
            runtimeEnumerations = 0
        }));
    }
}
