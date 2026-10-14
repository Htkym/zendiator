using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record PublicAsyncFixture(Type Request, Action<IServiceCollection> Register,
        Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<int>> Stream);

    private static string PublicAsyncSource()
    {
        var source = AsyncItemFixtureSource();
        const string probe = "global::Zendiator.Generator.Tests.CanonicalStreamAsyncItemProbe";
        foreach (var (name, role, contract) in new[]
        {
            ("ItemsHandler", "H", "IStreamRequestHandler<Items,int>"),
            ("Positive", "F1", "IAsyncStreamItemFilter<Items,int>"),
            ("DoubleItem", "Map", "IAsyncStreamItemMap<Items,int>"),
            ("Small", "F2", "IStreamItemFilter<Items,int>")
        })
        {
            var declaration = "public sealed class " + name + "(" + probe + " p) : " + contract + " {";
            Assert.Contains(declaration, source);
            source = source.Replace(declaration, "public sealed class " + name + " : " + contract + ", IDisposable {" +
                "private readonly " + probe + " p; public " + name + "(" + probe + " probe){p=probe;p.Resolve(\"" + role + "\");}" +
                "public void Dispose()=>p.DisposeBorrowed(\"" + role + "\");");
        }
        return source + """
            public static class PublicAsyncEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<int> Stream(System.IServiceProvider scope,object request,CancellationToken token)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync((Items)request,token);
            }
            """;
    }

    private static System.Reflection.Assembly CompilePublicAsyncInput(string shape, string source)
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicAsync_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var folder = PublicAsyncFolder(shape);
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                shape,
                emitted.Success,
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
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        if (shape == "wrapper-blocked")
        {
            Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
            Assert.True(diagnostics[0].Location.IsInSource);
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.", StringComparison.Ordinal));
        }
        else
        {
            Assert.Empty(diagnostics);
            var prefix = shape == "sync-compile-only" ? "Zendiator.StreamPrototype." : "Zendiator.StreamCanonical.";
            var backend = Assert.Single(generated, f => f.HintName.StartsWith(prefix, StringComparison.Ordinal));
            var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
            Assert.Contains(shape == "sync-compile-only" ? "__ZendiatorStreamPrototype_" : "__ZendiatorStreamCanonical_", facade);
            Assert.DoesNotContain("ThrowIfCancellationRequested", facade);
            if (shape == "async-public")
            {
                Assert.Contains("AcceptAsync", backend.SourceText.ToString()); Assert.Contains("MapAsync", backend.SourceText.ToString());
                Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
            }
        }
        bytes.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(bytes);
    }

    [Fact]
    public async Task Stream_public_async_entry_preserves_completion_pending_cancellation_and_borrowed_scope()
    {
        CompilePublicAsyncInput("sync-compile-only", PublicStreamSource("accepted")); // No replay of v9's three synchronous enumerations.
        var assembly = CompilePublicAsyncInput("async-public", PublicAsyncSource());
        var entry = assembly.GetType("App.PublicAsyncEntry")!;
        // The test host loads typed consumer delegates; it never calls or reflects over internal Create.
        var fixture = new PublicAsyncFixture(assembly.GetType("App.Items")!,
            entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>(),
            entry.GetMethod("Stream")!.CreateDelegate<Func<IServiceProvider, object, CancellationToken, IAsyncEnumerable<int>>>());
        foreach (var profile in new[] { "completed", "pending-filter", "cancel-map" })
        {
            using var api = new CancellationTokenSource(); var request = Activator.CreateInstance(fixture.Request)!;
            var events = new StreamSeedEvents(request, api.Token);
            var probe = new CanonicalStreamAsyncItemProbe { Profile = profile, Request = request, Token = api.Token, Events = events };
            var services = new ServiceCollection(); services.AddSingleton(probe); fixture.Register(services);
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var scope = provider.CreateAsyncScope(); var cursor = fixture.Stream(scope.ServiceProvider, request, api.Token).GetAsyncEnumerator();
            Assert.Empty(events.Snapshot()); Assert.Empty(probe.BorrowedDisposals);
            var operation = cursor.MoveNextAsync(); var initiallyCompleted = operation.IsCompleted; var move = operation.AsTask();
            if (profile != "completed")
            {
                await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(move.IsCompleted);
                Assert.Equal(1, probe.Reads); Assert.Equal(0, probe.LastCalls);
                Assert.DoesNotContain(events.Snapshot(), e => e.Kind == "Resolve" && e.Stage == "F2");
                if (profile == "pending-filter")
                {
                    Assert.DoesNotContain(events.Snapshot(), e => e.Kind == "Resolve" && e.Stage == "Map");
                    probe.Event("Release", "F1", result: true); probe.FirstGate.Complete(true);
                }
                else
                {
                    Assert.Equal(1, probe.FirstGate.Consumptions); Assert.Equal(0, probe.MapGate.Consumptions);
                    api.Cancel(); probe.Event("Cancel", "Consumer"); probe.Event("Release", "Map", output: 2); probe.MapGate.Complete(2);
                }
            }
            var values = new List<int>(); Exception? error = null;
            try
            {
                if (await move.WaitAsync(TimeSpan.FromSeconds(5)))
                {
                    values.Add(cursor.Current); probe.Event("ConsumerYield", "Consumer", output: cursor.Current);
                    Assert.False(await cursor.MoveNextAsync());
                }
            }
            catch (Exception caught) { error = caught; probe.Event("ConsumerCatch", "Consumer", error: caught); }
            await cursor.DisposeAsync();
            Assert.Equal(profile == "completed", initiallyCompleted); Assert.Equal(1, probe.Starts); Assert.Equal(1, probe.Reads);
            Assert.Equal(1, probe.Disposals); Assert.Equal(1, probe.FirstGate.Consumptions); Assert.Equal(1, probe.MapGate.Consumptions);
            Assert.Equal(profile == "cancel-map" ? 0 : 1, probe.LastCalls);
            Assert.Equal(profile == "cancel-map" ? 1 : 2, probe.Moves);
            if (profile == "cancel-map")
            {
                var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error); Assert.Equal(api.Token, canceled.CancellationToken);
                Assert.Empty(values); Assert.True(move.IsCanceled);
                Assert.DoesNotContain(events.Snapshot(), e => e.Kind == "Resolve" && e.Stage == "F2");
            }
            else { Assert.Null(error); Assert.Equal(new[] { 2 }, values); }
            Assert.Empty(probe.BorrowedDisposals); var beforeScope = events.Snapshot();
            await scope.DisposeAsync();
            var expected = profile == "cancel-map" ? new[] { "F1", "H", "Map" } : new[] { "F1", "F2", "H", "Map" };
            Assert.Equal(expected, probe.BorrowedDisposals.Order(StringComparer.Ordinal));
            var folder = PublicAsyncFolder(profile);
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                profile,
                values,
                initiallyCompleted,
                canceled = move.IsCanceled,
                faulted = move.IsFaulted,
                probe.Starts,
                probe.Moves,
                probe.Reads,
                probe.Disposals,
                probe.LastCalls,
                firstConsumptions = probe.FirstGate.Consumptions,
                mapConsumptions = probe.MapGate.Consumptions,
                borrowedDisposalsBeforeScope = 0,
                borrowedDisposalsAfterScope = probe.BorrowedDisposals,
                eventsBeforeScope = beforeScope,
                events = events.Snapshot()
            }));
        }
        // Same async callbacks behind an opaque wrapper remain internal; no extra runtime matrix.
        var blocked = AsyncItemFixtureSource().Replace("Stage.Hook<Small>()", "Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()");
        CompilePublicAsyncInput("wrapper-blocked", blocked);
    }

    private static string? PublicAsyncFolder(string shape)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_ASYNC_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, shape); Directory.CreateDirectory(folder); return folder;
    }
}
