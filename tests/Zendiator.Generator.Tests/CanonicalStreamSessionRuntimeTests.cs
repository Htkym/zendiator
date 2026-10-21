using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record SessionRuntimeFixture(bool Owned, Type Request, Dictionary<string, Type> Types, MethodInfo Create);
    private sealed record SessionRuntimeResult(int[] Values, string[] Events, string[] Created, int[] GetResults,
        int Enumerations, int Starts, int Moves, int Reads, int SourceDisposals, int SessionDisposals, int ServiceDisposals,
        bool FactoryErrorIdentity, bool LazyBeforeMove, bool HeldUntilDispose);
    private static string SessionRuntimeSource(bool owned)
    {
        var handler = owned ? "HandlerEntry.Session<SessionHandler,HandlerFactory>()" : "typeof(SessionHandler)";
        var first = owned ? "Stage.Hook<FirstSession,FirstFactory>()" : "Stage.Hook<FirstSession>()";
        var last = owned ? "Stage.Hook<LastSession,LastFactory>()" : "Stage.Hook<LastSession>()";
        return ListFixture("c.Handlers<SessionRequest>(DispatchFamily.Stream," + handler + ");c.Pipeline<SessionRequest>(" + first + ",Stage.Hook<BorrowedMiddle>()," + last + ");") + """
            public sealed record SessionRequest:IStreamRequest<int>;
            [ZendiatorHandler] public sealed class SessionHandler(global::Zendiator.Generator.Tests.CanonicalStreamSessionProbe p,int id):IStreamRequestHandler<SessionRequest,int>,IAsyncDisposable {
                public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(SessionRequest r,CancellationToken c)=>p.Start(id,r,c);
                public ValueTask DisposeAsync()=>p.DisposeSession("H",id);
            }
            public sealed class FirstSession(global::Zendiator.Generator.Tests.CanonicalStreamSessionProbe p,int id):IStreamItemMap<SessionRequest,int>,IAsyncDisposable {
                public int Map(SessionRequest r,int item,CancellationToken c)=>p.Map("K1",id,r,item,c);
                public ValueTask DisposeAsync()=>p.DisposeSession("K1",id);
            }
            public sealed class LastSession(global::Zendiator.Generator.Tests.CanonicalStreamSessionProbe p,int id):IStreamItemMap<SessionRequest,int>,IAsyncDisposable {
                public int Map(SessionRequest r,int item,CancellationToken c)=>p.Map("K2",id,r,item,c);
                public ValueTask DisposeAsync()=>p.DisposeSession("K2",id);
            }
            public sealed class BorrowedMiddle(global::Zendiator.Generator.Tests.CanonicalStreamSessionProbe p):IStreamItemMap<SessionRequest,int>,IDisposable {
                public int Map(SessionRequest r,int item,CancellationToken c)=>p.Map("B",0,r,item,c);
                public void Dispose()=>p.ReleaseService("B");
            }
            public sealed class HandlerFactory(global::Zendiator.Generator.Tests.CanonicalStreamSessionProbe p):IOwnedSessionFactory<SessionRequest,SessionHandler>,IDisposable {
                public SessionHandler Create(SessionRequest r,CancellationToken c)=>p.Create("H",r,c,id=>new SessionHandler(p,id));
                public void Dispose()=>p.ReleaseService("H.F");
            }
            public sealed class FirstFactory(global::Zendiator.Generator.Tests.CanonicalStreamSessionProbe p):IAsyncOwnedSessionFactory<SessionRequest,FirstSession>,IDisposable {
                public ValueTask<FirstSession> CreateAsync(SessionRequest r,CancellationToken c)=>p.CreateAsync("K1",r,c,id=>new FirstSession(p,id));
                public void Dispose()=>p.ReleaseService("K1.F");
            }
            public sealed class LastFactory(global::Zendiator.Generator.Tests.CanonicalStreamSessionProbe p):IOwnedSessionFactory<SessionRequest,LastSession>,IDisposable {
                public LastSession Create(SessionRequest r,CancellationToken c)=>p.Create("K2",r,c,id=>new LastSession(p,id));
                public void Dispose()=>p.ReleaseService("K2.F");
            }
            """;
    }
    [Fact]
    public async Task Stream_owned_sessions_are_lazy_fresh_reverse_released_and_separate_from_DI_scope()
    {
        var owned = CompileSessionRuntime(true); var stateless = CompileSessionRuntime(false);
        foreach (var profile in new[] { "reenumerate", "partial-startup", "stateless" })
        {
            var fixture = profile == "stateless" ? stateless : owned;
            var reference = await RunSessionRuntime(fixture, profile, true); var canonical = await RunSessionRuntime(fixture, profile, false);
            SaveSessionRuntime(profile, new { reference, canonical, productionEnabled = false });
            Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Events, canonical.Events);
            Assert.Equal(reference.Created, canonical.Created); Assert.Equal(reference.GetResults, canonical.GetResults);
            Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events, Created = canonical.Created, GetResults = canonical.GetResults }, canonical);
            Assert.Equal(profile == "partial-startup" ? [] : profile == "reenumerate" ? new[] { 21, 21 } : new[] { 21 }, canonical.Values);
            Assert.Equal(profile == "stateless" ? 0 : profile == "reenumerate" ? 6 : 2, canonical.SessionDisposals);
            Assert.Equal(profile == "partial-startup" ? 0 : canonical.Enumerations, canonical.SourceDisposals);
            Assert.Equal(profile == "partial-startup" ? 3 : 4, canonical.ServiceDisposals);
            Assert.True(canonical.LazyBeforeMove); Assert.True(canonical.HeldUntilDispose);
            Assert.Equal(profile == "partial-startup", canonical.FactoryErrorIdentity);
        }
    }
    private static SessionRuntimeFixture CompileSessionRuntime(bool owned)
    {
        var id = owned ? "owned" : "stateless"; var source = SessionRuntimeSource(owned);
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "SessionRuntime_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var graph = Graph(driver); var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes); var folder = SessionRuntimeFolder(id);
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "binding.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                id,
                emitted.Success,
                graph.IsValid,
                facts = graph.Facts,
                sessionOwners = graph.StreamSchedule!.Owners.Where(o => o.Kind == StreamOwnerKind.Session),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), inSource = d.Location.IsInSource }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                productionEnabled = false
            }));
        }
        if (owned) Assert.Empty(diagnostics);
        else Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var text = Assert.Single(generated, s => s.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)).SourceText.ToString();
        if (owned)
        {
            Assert.Equal(3, graph.StreamSchedule!.Owners.Count(o => o.Kind == StreamOwnerKind.Session));
            Assert.Contains("if (!_started) await Close()", text); Assert.Contains("_handler = null", text);
            Assert.DoesNotContain("typeof(global::App.SessionHandler)", text); Assert.DoesNotContain("typeof(global::App.FirstSession)", text);
            Assert.DoesNotContain("typeof(global::App.LastSession)", text);
        }
        else
        {
            Assert.DoesNotContain(graph.StreamSchedule!.Owners, o => o.Kind == StreamOwnerKind.Session);
            Assert.DoesNotContain("Factory)", text); Assert.DoesNotContain("factory returned null", text); Assert.DoesNotContain("private global::Zendiator.IStreamRequestHandler", text);
        }
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var names = new[] { "SessionHandler", "FirstSession", "BorrowedMiddle", "LastSession", "HandlerFactory", "FirstFactory", "LastFactory" };
        return new(owned, assembly.GetType("App.SessionRequest")!, names.ToDictionary(n => n, n => assembly.GetType("App." + n)!),
            Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal)).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!);
    }
    private static async Task<SessionRuntimeResult> RunSessionRuntime(SessionRuntimeFixture f, string profile, bool reference)
    {
        var request = Activator.CreateInstance(f.Request)!; var p = new CanonicalStreamSessionProbe { Request = request, FailHandler = profile == "partial-startup" };
        var services = new ServiceCollection(); var names = f.Owned ? new[] { "LastFactory", "FirstFactory", "HandlerFactory", "BorrowedMiddle" } : new[] { "SessionHandler", "FirstSession", "BorrowedMiddle", "LastSession" };
        var roles = f.Owned ? new[] { "K2.F", "K1.F", "H.F", "B" } : new[] { "H", "K1", "B", "K2" };
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i]; var role = roles[i]; var type = f.Types[name];
            services.AddScoped(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, name.EndsWith("Session", StringComparison.Ordinal) || name == "SessionHandler" ? new object[] { p, 0 } : new object[] { p })!; });
        }
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope(); IAsyncEnumerable<int> stream;
        if (reference)
        {
            var method = typeof(CanonicalStreamSessionReference).GetMethod(f.Owned ? "Owned" : "Stateless")!;
            var types = new[] { f.Request }.Concat(new[] { "SessionHandler", "FirstSession", "BorrowedMiddle", "LastSession" }.Select(n => f.Types[n]));
            if (f.Owned) types = types.Concat(new[] { "HandlerFactory", "FirstFactory", "LastFactory" }.Select(n => f.Types[n]));
            stream = (IAsyncEnumerable<int>)method.MakeGenericMethod(types.ToArray()).Invoke(null, [scope.ServiceProvider, request])!;
        }
        else stream = (IAsyncEnumerable<int>)f.Create.Invoke(null, [scope.ServiceProvider, request, default(CancellationToken)])!;
        var count = profile == "reenumerate" ? 2 : 1; var cursors = Enumerable.Range(0, count).Select(_ => stream.GetAsyncEnumerator()).ToArray();
        Assert.Empty(p.Events); Assert.Empty(p.Created); var values = new List<int>(); var errorIdentity = false;
        for (var i = 0; i < cursors.Length; i++)
        {
            p.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); var cursor = cursors[i]; var createdBefore = p.Created.Count;
            var move = cursor.MoveNextAsync().AsTask();
            if (f.Owned)
            {
                await p.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(move.IsCompleted);
                Assert.Equal(createdBefore + 1, p.Created.Count); Assert.Equal("K2", p.Created[^1].Role); Assert.Equal(0, p.Consumptions[^1]()); p.Release();
            }
            if (profile == "partial-startup")
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => { await move.WaitAsync(TimeSpan.FromSeconds(5)); });
                Assert.Same(p.FactoryError, error); errorIdentity = true; Assert.Equal(2, p.SessionDisposals); Assert.Equal(0, p.SourceDisposals); Assert.Equal(0, p.Starts);
            }
            else
            {
                Assert.True(await move.WaitAsync(TimeSpan.FromSeconds(5))); values.Add(cursor.Current); _ = cursor.Current;
                Assert.False(await cursor.MoveNextAsync());
                if (f.Owned) { Assert.Equal(i, p.SourceDisposals); Assert.Equal(i * 3, p.SessionDisposals); }
                else Assert.Equal(i + 1, p.SourceDisposals);
            }
            Assert.False(await cursor.MoveNextAsync()); Assert.Equal(0, p.ServiceDisposals);
            await cursor.DisposeAsync(); var events = p.Events.Count; await cursor.DisposeAsync(); Assert.Equal(events, p.Events.Count);
            if (f.Owned) Assert.All(p.Consumptions, c => Assert.Equal(1, c()));
        }
        var sessionDisposals = p.SessionDisposals; await scope.DisposeAsync(); Assert.Equal(sessionDisposals, p.SessionDisposals);
        if (f.Owned)
        {
            Assert.Equal(p.Instances.Count, p.Instances.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.Equal(profile == "reenumerate" ? new[] { "K2:1", "K1:2", "H:3", "K2:4", "K1:5", "H:6" } : new[] { "K2:1", "K1:2" }, p.Created.Select(x => x.Role + ":" + x.Id));
            Assert.Equal(profile == "reenumerate" ? new[] { "Session.Dispose:H:3", "Session.Dispose:K1:2", "Session.Dispose:K2:1", "Session.Dispose:H:6", "Session.Dispose:K1:5", "Session.Dispose:K2:4" } : new[] { "Session.Dispose:K1:2", "Session.Dispose:K2:1" }, p.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
            foreach (var h in p.Created.Where(x => x.Role == "H"))
                Assert.True(p.Events.IndexOf("H.Dispose:" + h.Id) < p.Events.IndexOf("Session.Dispose:H:" + h.Id));
        }
        return new([.. values], [.. p.Events], p.Created.Select(x => x.Role + ":" + x.Id).ToArray(), p.Consumptions.Select(c => c()).ToArray(),
            count, p.Starts, p.Moves, p.Reads, p.SourceDisposals, p.SessionDisposals, p.ServiceDisposals, errorIdentity, true, true);
    }
    private static string? SessionRuntimeFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "session-runtime", name); Directory.CreateDirectory(folder); return folder;
    }
    private static void SaveSessionRuntime(string name, object value)
    {
        var folder = SessionRuntimeFolder("results"); if (folder != null) File.WriteAllText(Path.Combine(folder, name + ".json"), System.Text.Json.JsonSerializer.Serialize(value));
    }
}
