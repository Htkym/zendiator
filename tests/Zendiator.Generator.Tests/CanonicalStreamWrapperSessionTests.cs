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
    private sealed record WrapperSessionFixture(Type Request, Dictionary<string, Type> Types, MethodInfo Create);
    private sealed record WrapperSessionResult(int[] Values, string[] Events, string[] Created, int Opens, int UpstreamEnumerations,
        int Starts, int Moves, int Reads, int SourceDisposals, int SessionDisposals, int ServiceDisposals,
        int WrapperFinallys, int InnerHeldChecks, int OuterAliveChecks, int OuterEnumerations = 1);
    private static string WrapperSessionSource()
    {
        var source = ListFixture("c.Handlers<BoundaryRequest>(DispatchFamily.Stream,HandlerEntry.Session<BoundaryHandler,HandlerFactory>());" +
            "c.Pipeline<BoundaryRequest>(Stage.Hook<InnerSession,InnerFactory>(),Stage.Wrapper<SessionWrapper,WrapperFactory>(),Stage.Hook<OuterSession,OuterFactory>());") + """
            public sealed record BoundaryRequest:IStreamRequest<int>;
            [ZendiatorHandler] public sealed class BoundaryHandler(global::Zendiator.Generator.Tests.CanonicalStreamWrapperSessionProbe p,int id):IStreamRequestHandler<BoundaryRequest,int>,IAsyncDisposable {
                public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(BoundaryRequest r,CancellationToken c)=>p.Start(id,r,c);
                public ValueTask DisposeAsync()=>p.DisposeSession("H",id);
            }
            public sealed class InnerSession(global::Zendiator.Generator.Tests.CanonicalStreamWrapperSessionProbe p,int id):IStreamItemMap<BoundaryRequest,int>,IAsyncDisposable {
                public int Map(BoundaryRequest r,int item,CancellationToken c)=>p.Map("I",id,r,item,c);
                public ValueTask DisposeAsync()=>p.DisposeSession("I",id);
            }
            public sealed class OuterSession(global::Zendiator.Generator.Tests.CanonicalStreamWrapperSessionProbe p,int id):IStreamItemMap<BoundaryRequest,int>,IAsyncDisposable {
                public int Map(BoundaryRequest r,int item,CancellationToken c)=>p.Map("O",id,r,item,c);
                public ValueTask DisposeAsync()=>p.DisposeSession("O",id);
            }
            public sealed class SessionWrapper(global::Zendiator.Generator.Tests.CanonicalStreamWrapperSessionProbe p,int id):IStreamWrapper<BoundaryRequest,int>,IAsyncDisposable {
                public System.Collections.Generic.IAsyncEnumerable<int> Wrap<N>(BoundaryRequest r,N n,CancellationToken c) where N:IStreamUpstream<BoundaryRequest,int> =>p.Wrap(r,n,id,c);
                public ValueTask DisposeAsync()=>p.DisposeSession("W",id);
            }
            """;
        foreach (var (role, receiver, factory) in new[] { ("H", "BoundaryHandler", "HandlerFactory"), ("I", "InnerSession", "InnerFactory"), ("W", "SessionWrapper", "WrapperFactory"), ("O", "OuterSession", "OuterFactory") })
            source += "public sealed class " + factory + "(global::Zendiator.Generator.Tests.CanonicalStreamWrapperSessionProbe p):IOwnedSessionFactory<BoundaryRequest," + receiver + ">,IDisposable {" +
                "public " + receiver + " Create(BoundaryRequest r,CancellationToken c)=>p.Create(\"" + role + "\",r,c,id=>new " + receiver + "(p,id));" +
                "public void Dispose()=>p.ReleaseService(\"" + role + ".F\");}";
        return source;
    }
    [Fact]
    public async Task Wrapper_sessions_skip_unopened_upstream_and_keep_fresh_inner_owners_separate_from_outer()
    {
        var fixture = CompileWrapperSession();
        foreach (var profile in new[] { "no-open", "two-enumerations" })
        {
            var reference = await RunWrapperSession(fixture, profile, true); var canonical = await RunWrapperSession(fixture, profile, false);
            SaveWrapperSession(profile, new { reference, canonical, productionEnabled = false });
            Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Events, canonical.Events); Assert.Equal(reference.Created, canonical.Created);
            Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events, Created = canonical.Created }, canonical);
            var opens = profile == "no-open" ? 0 : 1;
            Assert.Equal(opens == 0 ? new[] { 101 } : new[] { 3, 3 }, canonical.Values); Assert.Equal(opens, canonical.Opens);
            Assert.Equal(opens * 2, canonical.UpstreamEnumerations); Assert.Equal(opens * 2, canonical.Starts); Assert.Equal(opens * 2, canonical.SourceDisposals);
            Assert.Equal(opens * 2, canonical.InnerHeldChecks); Assert.Equal(opens * 2, canonical.OuterAliveChecks); Assert.Equal(1, canonical.WrapperFinallys);
            Assert.Equal(opens == 0 ? 2 : 6, canonical.SessionDisposals); Assert.Equal(opens == 0 ? 2 : 4, canonical.ServiceDisposals);
        }
    }
    private static WrapperSessionFixture CompileWrapperSession()
    {
        var source = WrapperSessionSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "WrapperSession_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var graph = Graph(driver); var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var folder = WrapperSessionFolder("input");
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "binding.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                emitted.Success,
                graph.IsValid,
                facts = graph.Facts,
                segments = graph.StreamWrapperSchedule!.Segments.Select(s => new { s.Descriptor.SourcePosition, s.Descriptor.HookPositions, sessionOwners = s.Cursor.Owners.Where(o => o.Kind == StreamOwnerKind.Session) }),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), inSource = d.Location.IsInSource }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                productionEnabled = false
            }));
        }
        Assert.Empty(diagnostics);
        Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        Assert.Equal(2, graph.StreamWrapperSchedule!.Segments.Count); Assert.All(graph.StreamWrapperSchedule.Segments, s => Assert.Equal(2, s.Cursor.Owners.Count(o => o.Kind == StreamOwnerKind.Session)));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var text = Assert.Single(generated, s => s.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("_wrapper = null", text); Assert.Contains("_handler = null", text); Assert.Contains("_wrapper!", text);
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var names = new[] { "BoundaryHandler", "InnerSession", "SessionWrapper", "OuterSession", "HandlerFactory", "InnerFactory", "WrapperFactory", "OuterFactory" };
        return new(assembly.GetType("App.BoundaryRequest")!, names.ToDictionary(n => n, n => assembly.GetType("App." + n)!),
            Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal)).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!);
    }
    private static async Task<WrapperSessionResult> RunWrapperSession(WrapperSessionFixture f, string profile, bool reference)
    {
        var request = Activator.CreateInstance(f.Request)!; var p = new CanonicalStreamWrapperSessionProbe { Request = request, NoOpen = profile == "no-open" };
        var services = new ServiceCollection(); foreach (var (name, role) in new[] { ("OuterFactory", "O.F"), ("WrapperFactory", "W.F"), ("InnerFactory", "I.F"), ("HandlerFactory", "H.F") })
        { var type = f.Types[name]; services.AddScoped(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, p)!; }); }
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope(); IAsyncEnumerable<int> stream;
        if (reference)
        {
            var types = new[] { f.Request }.Concat(new[] { "BoundaryHandler", "InnerSession", "SessionWrapper", "OuterSession", "HandlerFactory", "InnerFactory", "WrapperFactory", "OuterFactory" }.Select(n => f.Types[n])).ToArray();
            stream = (IAsyncEnumerable<int>)typeof(CanonicalStreamWrapperSessionReference).GetMethod("Create")!.MakeGenericMethod(types).Invoke(null, [scope.ServiceProvider, request])!;
        }
        else stream = (IAsyncEnumerable<int>)f.Create.Invoke(null, [scope.ServiceProvider, request, default(CancellationToken)])!;
        var cursor = stream.GetAsyncEnumerator(); Assert.Empty(p.Events); Assert.Empty(p.Active); var values = new List<int>();
        while (await cursor.MoveNextAsync()) { values.Add(cursor.Current); _ = cursor.Current; }
        Assert.False(await cursor.MoveNextAsync()); Assert.Equal(2, p.Active.Count); Assert.All(p.Active.Values, r => Assert.True(r is "O" or "W")); Assert.Equal(0, p.ServiceDisposals);
        await cursor.DisposeAsync(); var events = p.Events.Count; await cursor.DisposeAsync(); Assert.Equal(events, p.Events.Count); Assert.Empty(p.Active);
        Assert.Equal(p.Created.Count, p.Instances.Distinct(ReferenceEqualityComparer.Instance).Count());
        var expected = profile == "no-open" ? new[] { "O:1", "W:2" } : new[] { "O:1", "W:2", "I:3", "H:4", "I:5", "H:6" };
        Assert.Equal(expected, p.Created.Select(x => x.Role + ":" + x.Id));
        Assert.Equal(profile == "no-open" ? new[] { "Session.Dispose:W:2", "Session.Dispose:O:1" } : new[] { "Session.Dispose:H:4", "Session.Dispose:I:3", "Session.Dispose:H:6", "Session.Dispose:I:5", "Session.Dispose:W:2", "Session.Dispose:O:1" }, p.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
        foreach (var h in p.Created.Where(x => x.Role == "H")) Assert.True(p.Events.IndexOf("H.Dispose:" + h.Id) < p.Events.IndexOf("Session.Dispose:H:" + h.Id));
        var disposed = p.SessionDisposals; await scope.DisposeAsync(); Assert.Equal(disposed, p.SessionDisposals);
        return new([.. values], [.. p.Events], p.Created.Select(x => x.Role + ":" + x.Id).ToArray(), p.Opens, p.UpstreamEnumerations, p.Starts, p.Moves, p.Reads, p.SourceDisposals, p.SessionDisposals, p.ServiceDisposals, p.WrapperFinallys, p.InnerHeldChecks, p.OuterAliveChecks);
    }
    private static string? WrapperSessionFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "wrapper-session", name); Directory.CreateDirectory(folder); return folder;
    }
    private static void SaveWrapperSession(string name, object value)
    {
        var folder = WrapperSessionFolder("results"); if (folder != null) File.WriteAllText(Path.Combine(folder, name + ".json"), System.Text.Json.JsonSerializer.Serialize(value));
    }
}
