using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private static string PublicTypedWrapperSource()
    {
        var source = TypedIntegrationSource("large-wrapper-session");
        const string probe = "global::Zendiator.Generator.Tests.CanonicalStreamTypedIntegrationProbe";
        foreach (var (name, role) in new[] { ("BigHandlerFactory", "H"), ("ToLargeFactory", "A"), ("BigWrapperFactory", "W"), ("LastLargeFactory", "B") })
        {
            var declaration = "public sealed class " + name + "(" + probe + " p):";
            Assert.Contains(declaration, source);
            var replacement = "public sealed class " + name + ":"; source = source.Replace(declaration, replacement);
            var body = source.IndexOf('{', source.IndexOf(replacement, StringComparison.Ordinal));
            source = source.Insert(body + 1, "private readonly " + probe + " p;public " + name + "(" + probe +
                " probe){p=probe;p.Resolve(\"" + role + "\");}");
        }
        return source + """
            public static class PublicTypedWrapperEntry {
                public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)=>Startup.Configure(services);
                public static System.Collections.Generic.IAsyncEnumerable<LargeItem> Stream(System.IServiceProvider scope,BigRequest request)=>
                    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::App.Generated.Reads.IZendiator>(scope)
                        .StreamAsync(request);
                public static async System.Threading.Tasks.Task<long[]> Run(System.IServiceProvider scope,object request) {
                    // This independently compiled consumer carries the 128B item as LargeItem throughout.
                    System.Collections.Generic.IAsyncEnumerable<LargeItem> stream=Stream(scope,(BigRequest)request);
                    await using System.Collections.Generic.IAsyncEnumerator<LargeItem> cursor=stream.GetAsyncEnumerator();
                    if(!await cursor.MoveNextAsync()) throw new System.InvalidOperationException("Missing item.");
                    LargeItem item=cursor.Current;
                    if(item.Value!=701 || await cursor.MoveNextAsync()) throw new System.InvalidOperationException("Unexpected output.");
                    return new long[]{item.Value};
                }
            }
            """;
    }

    private static Assembly? CompilePublicTypedWrapper(string shape, string source)
    {
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "PublicTypedWrapper_" + Guid.NewGuid().ToString("N")),
            out var output, out var diagnostics);
        var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var folder = PublicTypedWrapperFolder(shape);
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
        if (shape == "interval-mismatch")
        {
            Assert.Contains(diagnostics, d => d.Id == "ZEN0026" && d.Severity == DiagnosticSeverity.Error && d.Location.IsInSource &&
                d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan).StartsWith("Stage.Wrapper", StringComparison.Ordinal));
            Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.", StringComparison.Ordinal)); return null;
        }
        Assert.Empty(diagnostics);
        var route = Assert.Single(Selection(driver).Pipelines); var flow = Assert.IsType<global::Zendiator.SourceGenerator.BoundStreamItemFlow>(route.StreamItemFlow);
        Assert.Equal("int", flow.Source.Name); Assert.Equal("global::App.LargeItem", flow.Final.Name);
        Assert.Equal(3, flow.Stages.Count); Assert.All(flow.Stages, s => Assert.Equal(flow.Final.Identity, s.Output.Identity));
        Assert.Equal(flow.Source.Identity, flow.Stages[0].Input.Identity);
        Assert.All(flow.Stages.Skip(1), s => Assert.Equal(flow.Final.Identity, s.Input.Identity));
        var facade = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.Canonical.Stream.Public.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("IAsyncEnumerable<global::App.LargeItem> StreamAsync(", facade);
        Assert.Contains("expected.StreamPipeline<global::App.BigRequest, global::App.LargeItem>", facade);
        Assert.Contains("HandlerEntry.Session<global::App.BigHandler, global::App.BigHandlerFactory>()", facade);
        foreach (var (receiver, factory, kind) in new[] { ("ToLarge", "ToLargeFactory", "Hook"), ("BigWrapper", "BigWrapperFactory", "Wrapper"), ("LastLarge", "LastLargeFactory", "Hook") })
        {
            Assert.Contains("Stage." + kind + "(typeof(global::App." + receiver + "), typeof(global::App." + factory + "))", facade);
            Assert.Contains("ServiceDescriptor.Scoped(typeof(global::App." + factory + "), typeof(global::App." + factory + "))", facade);
        }
        Assert.Contains("ServiceDescriptor.Scoped(typeof(global::App.BigHandlerFactory), typeof(global::App.BigHandlerFactory))", facade);
        Assert.Contains("__ZendiatorStreamCanonical_", facade); Assert.DoesNotContain("IAsyncEnumerable<object>", facade);
        var backend = Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal)).SourceText.ToString();
        Assert.Contains("IAsyncEnumerator<int>? _source", backend); Assert.Contains("IAsyncEnumerator<global::App.LargeItem>? _source", backend);
        Assert.Contains("IStreamUpstream<global::App.BigRequest, global::App.LargeItem>", backend);
        Assert.DoesNotContain("object _current", backend); Assert.DoesNotContain("IAsyncEnumerator<object>", backend);
        bytes.Position = 0; return AssemblyLoadContext.Default.LoadFromStream(bytes);
    }

    [Fact]
    public async Task Stream_public_typed_wrapper_sessions_preserve_large_item_ports_and_separate_DI_owners()
    {
        CompilePublicTypedWrapper("interval-mismatch", TypedIntegrationSource("interval-mismatch"));
        var assembly = CompilePublicTypedWrapper("public", PublicTypedWrapperSource())!; var entry = assembly.GetType("App.PublicTypedWrapperEntry")!;
        var register = entry.GetMethod("Register")!.CreateDelegate<Action<IServiceCollection>>();
        var run = entry.GetMethod("Run")!.CreateDelegate<Func<IServiceProvider, object, Task<long[]>>>();
        var final = assembly.GetType("App.LargeItem")!; Assert.Equal(128, Marshal.SizeOf(final));
        var api = assembly.GetType("App.Generated.Reads.IZendiator")!.GetMethod("StreamAsync")!;
        Assert.Equal(typeof(IAsyncEnumerable<>).MakeGenericType(final), api.ReturnType);
        var helper = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal));
        var cursors = helper.GetNestedTypes(BindingFlags.NonPublic).Where(t => t.Name.StartsWith("Cursor", StringComparison.Ordinal)).OrderBy(t => t.Name).ToArray();
        Assert.Equal(2, cursors.Length);
        Assert.All(cursors, t => { Assert.Equal(final, t.GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType); Assert.Equal(final, t.GetProperty("Current")!.PropertyType); });
        Assert.Equal(typeof(IAsyncEnumerator<int>), cursors[0].GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType);
        Assert.Equal(typeof(IAsyncEnumerator<>).MakeGenericType(final), cursors[1].GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType);
        var request = Activator.CreateInstance(assembly.GetType("App.BigRequest")!)!;
        var probe = new CanonicalStreamTypedIntegrationProbe { Request = request };
        var services = new ServiceCollection(); services.AddSingleton(probe); register(services);
        foreach (var name in new[] { "BigHandlerFactory", "ToLargeFactory", "BigWrapperFactory", "LastLargeFactory" })
            Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == assembly.GetType("App." + name)).Lifetime);
        foreach (var name in new[] { "BigHandler", "ToLarge", "BigWrapper", "LastLarge" })
            Assert.DoesNotContain(services, d => d.ServiceType == assembly.GetType("App." + name));
        Assert.Empty(probe.Events);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var scope = provider.CreateAsyncScope(); var values = await run(scope.ServiceProvider, request);
        Assert.Equal(new long[] { 701 }, values); Assert.Equal(1, probe.Starts); Assert.Equal(1, probe.SourceEnumerations);
        Assert.Equal(2, probe.Moves); Assert.Equal(1, probe.Reads); Assert.Equal(1, probe.SourceDisposals);
        Assert.Equal(new[] { "B:1", "W:2", "A:3", "H:4" }, probe.Created.Select(c => c.Role + ":" + c.Id));
        Assert.Equal(4, probe.Instances.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(new[] { "Session.Dispose:H:4", "Session.Dispose:A:3", "Session.Dispose:W:2", "Session.Dispose:B:1" }, probe.Events.Where(e => e.StartsWith("Session.Dispose:", StringComparison.Ordinal)));
        Assert.True(probe.Events.IndexOf("H.Dispose") < probe.Events.IndexOf("Session.Dispose:H:4"));
        Assert.Equal(4, probe.SessionDisposals); Assert.Equal(0, probe.FactoryDisposals);
        var beforeScope = probe.Events.ToArray(); await scope.DisposeAsync(); Assert.Equal(4, probe.SessionDisposals); Assert.Equal(4, probe.FactoryDisposals);
        var folder = PublicTypedWrapperFolder("public");
        if (folder != null) File.WriteAllText(Path.Combine(folder, "runtime.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            values, finalItem = final.FullName, itemBytes = 128, runtimeEnumerations = 1, innerEnumerations = 1,
            publicReturnType = api.ReturnType.ToString(), cursorFields = cursors.Select(t => new { cursor = t.Name,
                current = t.GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType.ToString(),
                source = t.GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.FieldType.ToString() }),
            probe.Starts, probe.Moves, probe.Reads, probe.SourceEnumerations, probe.SourceDisposals,
            created = probe.Created.Select(c => c.Role + ":" + c.Id), distinctSessionInstances = 4,
            sessionDisposalsBeforeScope = 4, sessionDisposalsAfterScope = probe.SessionDisposals,
            borrowedFactoryDisposalsBeforeScope = 0, borrowedFactoryDisposalsAfterScope = probe.FactoryDisposals,
            typedConsumerNoObjectItemAdapter = true, requestTokenCheckedByProbe = true, eventsBeforeScope = beforeScope, events = probe.Events
        }));
    }

    private static string? PublicTypedWrapperFolder(string shape)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_PUBLIC_TYPED_WRAPPER_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, shape); Directory.CreateDirectory(folder); return folder;
    }
}
