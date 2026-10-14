using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string PublicPlainSessionSource()
    {
        var source = SessionRuntimeSource(true);
        const string probe = "global::Zendiator.Generator.Tests.CanonicalStreamSessionProbe";
        foreach (var (name, role) in new[] { ("HandlerFactory", "H.F"), ("FirstFactory", "K1.F"), ("LastFactory", "K2.F"), ("BorrowedMiddle", "B") })
        {
            var declaration = "public sealed class " + name + "(" + probe + " p):";
            Assert.Contains(declaration, source);
            var replacement = "public sealed class " + name + ":";
            source = source.Replace(declaration, replacement);
            var body = source.IndexOf('{', source.IndexOf(replacement, StringComparison.Ordinal));
            source = source.Insert(body + 1, "private readonly " + probe + " p;public " + name + "(" + probe +
                " probe){p=probe;p.Resolve(\"" + role + "\");}");
        }
        return source + """
            public static class PublicSessionEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<int> Stream(System.IServiceProvider scope,object request)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync((SessionRequest)request);
            }
            """;
    }

    [Fact]
    public async Task Stream_public_plain_owned_sessions_are_fresh_cleanup_partial_start_and_leave_factories_to_scope()
    {
        var source = PublicPlainSessionSource();
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicSession_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var inputFolder = PublicSessionFolder("input");
        if (inputFolder != null)
        {
            File.WriteAllText(Path.Combine(inputFolder, "input.cs"), source); File.WriteAllBytes(Path.Combine(inputFolder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(inputFolder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(inputFolder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                emitted.Success, composition = Selection(driver),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }), sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.Empty(diagnostics); Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("HandlerEntry.Session<global::App.SessionHandler, global::App.HandlerFactory>()", facade);
        Assert.Contains("Stage.Hook<global::App.FirstSession, global::App.FirstFactory>()", facade);
        Assert.Contains("__ZendiatorStreamCanonical_", facade);
        Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var entry = assembly.GetType("App.PublicSessionEntry")!;
        var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var streamEntry = entry.GetMethod("Stream")!.CreateDelegate<Func<IServiceProvider, object, IAsyncEnumerable<int>>>();
        foreach (var profile in new[] { "reenumerate", "partial-startup" })
        {
            var request = Activator.CreateInstance(assembly.GetType("App.SessionRequest")!)!;
            var probe = new CanonicalStreamSessionProbe { Request = request, FailHandler = profile == "partial-startup" };
            var services = new ServiceCollection(); services.AddSingleton(probe); register(services);
            var registered = new[] { "HandlerFactory", "FirstFactory", "LastFactory", "BorrowedMiddle" }.Select(n => assembly.GetType("App." + n)!).ToArray();
            Assert.All(registered, type => Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == type).Lifetime));
            foreach (var name in new[] { "SessionHandler", "FirstSession", "LastSession" })
                Assert.DoesNotContain(services, d => d.ServiceType == assembly.GetType("App." + name));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var scope = provider.CreateAsyncScope(); var stream = streamEntry(scope.ServiceProvider, request);
            var count = profile == "reenumerate" ? 2 : 1;
            var cursors = Enumerable.Range(0, count).Select(_ => stream.GetAsyncEnumerator()).ToArray();
            Assert.Empty(probe.Events); Assert.Empty(probe.Instances); var values = new List<int>(); var factoryErrorIdentity = false;
            for (var i = 0; i < cursors.Length; i++)
            {
                probe.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); var cursor = cursors[i];
                var createdBefore = probe.Created.Count; var move = cursor.MoveNextAsync().AsTask();
                await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(move.IsCompleted);
                Assert.Equal(createdBefore + 1, probe.Created.Count); Assert.Equal("K2", probe.Created[^1].Role);
                Assert.Equal(0, probe.Consumptions[^1]()); probe.Release();
                if (profile == "partial-startup")
                {
                    var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => { await move.WaitAsync(TimeSpan.FromSeconds(5)); });
                    Assert.Same(probe.FactoryError, error); factoryErrorIdentity = true;
                    Assert.Equal(2, probe.SessionDisposals); Assert.Equal(0, probe.SourceDisposals); Assert.Equal(0, probe.Starts);
                }
                else
                {
                    Assert.True(await move.WaitAsync(TimeSpan.FromSeconds(5))); values.Add(cursor.Current);
                    Assert.False(await cursor.MoveNextAsync());
                    Assert.Equal(i, probe.SourceDisposals); Assert.Equal(i * 3, probe.SessionDisposals);
                }
                Assert.Equal(0, probe.ServiceDisposals); await cursor.DisposeAsync();
                Assert.All(probe.Consumptions, consumption => Assert.Equal(1, consumption()));
            }
            var expectedCreated = profile == "reenumerate" ? new[] { "K2:1", "K1:2", "H:3", "K2:4", "K1:5", "H:6" } : new[] { "K2:1", "K1:2" };
            Assert.Equal(expectedCreated, probe.Created.Select(c => c.Role + ":" + c.Id));
            Assert.Equal(probe.Instances.Count, probe.Instances.Distinct(ReferenceEqualityComparer.Instance).Count());
            var expectedDisposed = profile == "reenumerate" ? new[] { "Session.Dispose:H:3", "Session.Dispose:K1:2", "Session.Dispose:K2:1", "Session.Dispose:H:6", "Session.Dispose:K1:5", "Session.Dispose:K2:4" } : new[] { "Session.Dispose:K1:2", "Session.Dispose:K2:1" };
            Assert.Equal(expectedDisposed, probe.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
            foreach (var handler in probe.Created.Where(c => c.Role == "H"))
                Assert.True(probe.Events.IndexOf("H.Dispose:" + handler.Id) < probe.Events.IndexOf("Session.Dispose:H:" + handler.Id));
            Assert.Equal(profile == "reenumerate" ? new[] { 21, 21 } : [], values);
            Assert.Equal(profile == "reenumerate" ? 6 : 2, probe.SessionDisposals); Assert.Equal(profile == "reenumerate" ? 2 : 0, probe.SourceDisposals);
            Assert.Equal(0, probe.ServiceDisposals); var beforeScope = probe.Events.ToArray(); var sessionDisposals = probe.SessionDisposals;
            await scope.DisposeAsync(); Assert.Equal(sessionDisposals, probe.SessionDisposals);
            Assert.Equal(profile == "reenumerate" ? 4 : 3, probe.ServiceDisposals);
            Assert.Equal(profile == "reenumerate" ? new[] { "DI.Dispose:B", "DI.Dispose:H.F", "DI.Dispose:K1.F", "DI.Dispose:K2.F" } : new[] { "DI.Dispose:H.F", "DI.Dispose:K1.F", "DI.Dispose:K2.F" },
                probe.Events.Where(e => e.StartsWith("DI.Dispose:", StringComparison.Ordinal)));
            var folder = PublicSessionFolder(profile);
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                profile, values, enumerations = count, created = expectedCreated, distinctSessionInstances = probe.Instances.Distinct(ReferenceEqualityComparer.Instance).Count(),
                probe.Starts, probe.Moves, probe.Reads, probe.SourceDisposals, probe.SessionDisposals,
                getResults = probe.Consumptions.Select(c => c()), borrowedServiceDisposalsBeforeScope = 0, borrowedServiceDisposalsAfterScope = probe.ServiceDisposals,
                factoryErrorIdentity, lazyBeforeMove = true, ownedHeldUntilCursorDispose = profile == "reenumerate", eventsBeforeScope = beforeScope, events = probe.Events
            }));
        }
    }

    private static string? PublicSessionFolder(string shape)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_PUBLIC_SESSION_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, shape); Directory.CreateDirectory(folder); return folder;
    }
}
