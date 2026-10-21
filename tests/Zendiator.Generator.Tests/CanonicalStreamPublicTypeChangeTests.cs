using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static Assembly? CompilePublicItemChange(string shape, string source)
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicItemChange_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var folder = PublicItemChangeFolder(shape);
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                shape,
                emitted.Success,
                composition = Selection(driver),
                diagnostics = diagnostics.Select(d => new
                {
                    d.Id,
                    severity = d.Severity.ToString(),
                    message = d.GetMessage(),
                    inSource = d.Location.IsInSource,
                    anchor = d.Location.IsInSource ? d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) : null
                }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        if (shape == "final-mismatch")
        {
            Assert.Contains(diagnostics, d => d.Id == "ZEN0026" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource &&
                d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan).StartsWith("c.StreamPipeline", StringComparison.Ordinal));
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.", StringComparison.Ordinal)); return null;
        }
        Assert.Empty(diagnostics);
        var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("IAsyncEnumerable<long> StreamAsync(", facade);
        Assert.Contains("expected.StreamPipeline<global::App.ChangeRequest, long>", facade);
        Assert.Contains("__ZendiatorStreamCanonical_", facade);
        Assert.DoesNotContain("IAsyncEnumerable<object>", facade); Assert.DoesNotContain("Cast<", facade);
        var backend = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("IAsyncEnumerable<long>", backend); Assert.Contains("IAsyncEnumerator<int>", backend);
        bytes.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(bytes);
    }

    [Fact]
    public async Task Stream_public_type_change_returns_bound_long_and_rejects_an_incompatible_final_port()
    {
        CompilePublicItemChange("final-mismatch", TypeChangeSource("final-mismatch"));
        var source = TypeChangeSource("success") + """
            public static class PublicChangeEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<long> Stream(System.IServiceProvider scope,object request)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync((ChangeRequest)request);
            }
            """;
        var assembly = CompilePublicItemChange("public", source)!; var entry = assembly.GetType("App.PublicChangeEntry")!;
        var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var streamEntry = entry.GetMethod("Stream")!.CreateDelegate<Func<IServiceProvider, object, IAsyncEnumerable<long>>>();
        var api = assembly.GetType("App.Generated.Reads.IZendiator")!.GetMethod("StreamAsync")!;
        Assert.Equal(typeof(IAsyncEnumerable<long>), api.ReturnType);
        var request = Activator.CreateInstance(assembly.GetType("App.ChangeRequest")!)!;
        var probe = new CanonicalStreamTypeChangeProbe { Request = request };
        var services = new ServiceCollection(); services.AddSingleton(probe); register(services);
        var receivers = new[] { assembly.GetType("App.ChangeHandler")!, assembly.GetType("App.ToReference`1")!.MakeGenericType(typeof(int)), assembly.GetType("App.ToFinal")! };
        Assert.All(receivers, type => Assert.Equal(ServiceLifetime.Transient, Assert.Single(services, d => d.ServiceType == type).Lifetime));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope(); IAsyncEnumerator<long> cursor = streamEntry(scope.ServiceProvider, request).GetAsyncEnumerator();
        Assert.Empty(probe.Trace); var move = cursor.MoveNextAsync().AsTask();
        await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(move.IsCompleted); Assert.Equal(0, probe.Gate.Consumptions);
        probe.Trace.Add("Release:long70"); probe.Gate.Complete(70L);
        Assert.True(await move.WaitAsync(TimeSpan.FromSeconds(5))); long value = cursor.Current; Assert.Equal(70L, value);
        Assert.False(await cursor.MoveNextAsync()); await cursor.DisposeAsync();
        Assert.Equal(1, probe.Starts); Assert.Equal(2, probe.Moves); Assert.Equal(1, probe.Reads); Assert.Equal(1, probe.Disposals);
        Assert.Equal(1, probe.SyncCalls); Assert.Equal(1, probe.AsyncCalls); Assert.Equal(1, probe.Gate.Consumptions);
        var folder = PublicItemChangeFolder("public");
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            values = new[] { value },
            publicReturnType = api.ReturnType.ToString(),
            cursorCurrentType = "System.Int64",
            cursorSourceType = "System.Int32",
            runtimeEnumerations = 1,
            pendingMap = true,
            requestTokenCheckedByProbe = true,
            probe.Starts,
            probe.Moves,
            probe.Reads,
            probe.Disposals,
            probe.SyncCalls,
            probe.AsyncCalls,
            getResults = probe.Gate.Consumptions,
            typedWithoutObjectAdapter = true,
            trace = probe.Trace
        }));
    }

    private static string? PublicItemChangeFolder(string shape)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_PUBLIC_ITEM_CHANGE_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, shape); Directory.CreateDirectory(folder); return folder;
    }
}
