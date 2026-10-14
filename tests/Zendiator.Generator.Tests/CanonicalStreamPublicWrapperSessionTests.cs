using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string PublicWrapperSessionSource()
    {
        var source = WrapperSessionSource();
        const string probe = "global::Zendiator.Generator.Tests.CanonicalStreamWrapperSessionProbe";
        foreach (var (name, role) in new[] { ("HandlerFactory", "H.F"), ("InnerFactory", "I.F"), ("WrapperFactory", "W.F"), ("OuterFactory", "O.F") })
        {
            var declaration = "public sealed class " + name + "(" + probe + " p):";
            Assert.Contains(declaration, source);
            var replacement = "public sealed class " + name + ":"; source = source.Replace(declaration, replacement);
            var body = source.IndexOf('{', source.IndexOf(replacement, StringComparison.Ordinal));
            source = source.Insert(body + 1, "private readonly " + probe + " p;public " + name + "(" + probe +
                " probe){p=probe;p.Resolve(\"" + role + "\");}");
        }
        return source + """
            public static class PublicWrapperEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<int> Stream(System.IServiceProvider scope,object request)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync((BoundaryRequest)request);
            }
            """;
    }

    private static System.Reflection.Assembly? CompilePublicWrapperSession(string shape, string source)
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicWrapper_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var folder = PublicWrapperSessionFolder(shape);
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                shape, emitted.Success, composition = Selection(driver),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage(), inSource = d.Location.IsInSource,
                    anchor = d.Location.IsInSource ? d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) : null }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }), sources = generated.Select(f => f.HintName)
            }));
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        if (shape == "mixed-blocked")
        {
            Assert.Equal(2, diagnostics.Length); Assert.All(diagnostics, d => { Assert.Equal("ZEN0037", d.Id); Assert.True(d.Location.IsInSource); });
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.", StringComparison.Ordinal)); return null;
        }
        Assert.Empty(diagnostics);
        var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("Stage.Wrapper<global::App.SessionWrapper, global::App.WrapperFactory>()", facade);
        Assert.Contains("Stage.Hook<global::App.InnerSession, global::App.InnerFactory>()", facade);
        Assert.Contains("Stage.Hook<global::App.OuterSession, global::App.OuterFactory>()", facade);
        Assert.Contains("__ZendiatorStreamCanonical_", facade);
        Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
        bytes.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(bytes);
    }

    [Fact]
    public async Task Stream_public_wrapper_sessions_skip_inner_or_open_twice_without_crossing_owner_boundaries()
    {
        var mixed = WrapperSessionSource().Replace("c.Handlers<BoundaryRequest>", "c.Handlers<OtherRequest>(DispatchFamily.Stream,typeof(OtherHandler));c.Handlers<BoundaryRequest>") + """
            public sealed record OtherRequest:IStreamRequest<int>;
            [ZendiatorHandler] public sealed class OtherHandler:IStreamRequestHandler<OtherRequest,int> {
                public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(OtherRequest r,CancellationToken c)=>throw new NotImplementedException();
            }
            """;
        CompilePublicWrapperSession("mixed-blocked", mixed);
        var assembly = CompilePublicWrapperSession("public", PublicWrapperSessionSource())!; var entry = assembly.GetType("App.PublicWrapperEntry")!;
        var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var streamEntry = entry.GetMethod("Stream")!.CreateDelegate<Func<IServiceProvider, object, IAsyncEnumerable<int>>>();
        foreach (var profile in new[] { "no-open", "two-open" })
        {
            var request = Activator.CreateInstance(assembly.GetType("App.BoundaryRequest")!)!;
            var probe = new CanonicalStreamWrapperSessionProbe { Request = request, NoOpen = profile == "no-open", OpenPerEnumeration = true };
            var services = new ServiceCollection(); services.AddSingleton(probe); register(services);
            var definitions = new[] { "HandlerFactory", "InnerFactory", "WrapperFactory", "OuterFactory" }.Select(name => assembly.GetType("App." + name)!).ToArray();
            Assert.All(definitions, type => Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == type).Lifetime));
            foreach (var name in new[] { "BoundaryHandler", "InnerSession", "SessionWrapper", "OuterSession" })
                Assert.DoesNotContain(services, d => d.ServiceType == assembly.GetType("App." + name));
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            var scope = provider.CreateAsyncScope(); var cursor = streamEntry(scope.ServiceProvider, request).GetAsyncEnumerator();
            Assert.Empty(probe.Events); Assert.Empty(probe.Active); var values = new List<int>();
            while (await cursor.MoveNextAsync()) values.Add(cursor.Current);
            Assert.Equal(2, probe.Active.Count); Assert.All(probe.Active.Values, role => Assert.True(role is "O" or "W"));
            Assert.Equal(0, probe.ServiceDisposals); await cursor.DisposeAsync(); Assert.Empty(probe.Active);
            var opened = profile == "two-open";
            var created = opened ? new[] { "O:1", "W:2", "I:3", "H:4", "I:5", "H:6" } : new[] { "O:1", "W:2" };
            Assert.Equal(created, probe.Created.Select(c => c.Role + ":" + c.Id));
            Assert.Equal(created.Length, probe.Instances.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.Equal(opened ? new[] { 3, 3 } : new[] { 101 }, values);
            Assert.Equal(opened ? 2 : 0, probe.Opens); Assert.Equal(opened ? 2 : 0, probe.UpstreamEnumerations);
            Assert.Equal(opened ? 2 : 0, probe.Starts); Assert.Equal(opened ? 2 : 0, probe.SourceDisposals);
            Assert.Equal(opened ? 2 : 0, probe.InnerHeldChecks); Assert.Equal(opened ? 2 : 0, probe.OuterAliveChecks); Assert.Equal(1, probe.WrapperFinallys);
            var ended = opened ? new[] { "Session.Dispose:H:4", "Session.Dispose:I:3", "Session.Dispose:H:6", "Session.Dispose:I:5", "Session.Dispose:W:2", "Session.Dispose:O:1" } : new[] { "Session.Dispose:W:2", "Session.Dispose:O:1" };
            Assert.Equal(ended, probe.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
            foreach (var h in probe.Created.Where(c => c.Role == "H"))
                Assert.True(probe.Events.IndexOf("H.Dispose:" + h.Id) < probe.Events.IndexOf("Session.Dispose:H:" + h.Id));
            if (!opened) Assert.DoesNotContain(probe.Events, e => e is "DI.Resolve:H.F" or "DI.Resolve:I.F");
            Assert.Equal(0, probe.ServiceDisposals); var beforeScope = probe.Events.ToArray(); var sessionDisposals = probe.SessionDisposals;
            await scope.DisposeAsync(); Assert.Equal(sessionDisposals, probe.SessionDisposals); Assert.Equal(opened ? 4 : 2, probe.ServiceDisposals);
            var folder = PublicWrapperSessionFolder(profile);
            if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                profile, values, outerEnumerations = 1, probe.Opens, probe.UpstreamEnumerations, created, distinctSessionInstances = probe.Instances.Distinct(ReferenceEqualityComparer.Instance).Count(),
                probe.Starts, probe.Moves, probe.Reads, probe.SourceDisposals, probe.SessionDisposals, probe.WrapperFinallys, probe.InnerHeldChecks, probe.OuterAliveChecks,
                lazyBeforeMove = true, borrowedFactoryDisposalsBeforeScope = 0, borrowedFactoryDisposalsAfterScope = probe.ServiceDisposals,
                sessionDisposalsBeforeScope = sessionDisposals, sessionDisposalsAfterScope = probe.SessionDisposals, eventsBeforeScope = beforeScope, events = probe.Events
            }));
        }
    }

    private static string? PublicWrapperSessionFolder(string shape)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_PUBLIC_WRAPPER_SESSION_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, shape); Directory.CreateDirectory(folder); return folder;
    }
}
