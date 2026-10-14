using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    [Fact]
    public void Stream_explicit_open_handler_compiles_and_reproduces_the_declared_snapshot()
    {
        var source = OpenAsyncRuntimeSource().Replace("c.Handlers(typeof(OpenAsyncRequest<>)",
            "c.AddHandler(typeof(OpenAsyncHandler<>));c.Handlers(typeof(OpenAsyncRequest<>)") + """
            public static class ExplicitOpenEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "ExplicitOpen_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_EXPLICIT_OPEN_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(root))
        {
            Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "input.cs"), source);
            File.WriteAllBytes(Path.Combine(root, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(root, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(root, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                emitted.Success, composition = Selection(driver),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                sources = generated.Select(f => f.HintName), runtimeEnumerations = 0
            }));
        }
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Equal(new[] { "global::App.OpenAsyncHandler<>" }, Selection(driver).ExplicitHandlerTypes);
        var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("expected.AddHandler(typeof(global::App.OpenAsyncHandler<>));", facade);
        Assert.DoesNotContain("expected.AddHandler<", facade);
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var register = assembly.GetType("App.ExplicitOpenEntry")!.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var services = new ServiceCollection(); register(services); // Exercises only the generated snapshot guard; no provider or stream.
        var definitions = new[] { "OpenAsyncHandler`1", "OpenAsyncFirst`2", "OpenAsyncMap`2", "OpenAsyncLast`2" }.Select(name => assembly.GetType("App." + name)!).ToArray();
        Assert.All(definitions, type => Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == type).Lifetime));
        if (!string.IsNullOrEmpty(root)) File.WriteAllText(Path.Combine(root, "snapshot-check.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            expectedExplicitCall = "expected.AddHandler(typeof(global::App.OpenAsyncHandler<>));",
            declaredSnapshotAcceptedByGeneratedGuard = true, registrationCalls = 1,
            registrations = services.Select(d => new { service = d.ServiceType.FullName, implementation = d.ImplementationType?.FullName, lifetime = d.Lifetime.ToString() }),
            providerBuilds = 0, handlerStarts = 0, runtimeEnumerations = 0, existingMatrixRerun = false
        }));
    }
}
