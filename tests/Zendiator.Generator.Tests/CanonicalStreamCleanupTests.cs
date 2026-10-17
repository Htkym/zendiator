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
    private sealed record CleanupFixture(Type Request, Dictionary<string, Type> Types, MethodInfo Create);
    private sealed record CleanupResult(int[] Values, string[] Events, int InnerCalls, int OuterCalls, int InnerConsumptions, int OuterConsumptions,
        int SourceDisposals, int BorrowedBeforeOwner, int BorrowedAfterOwner, int FactoriesBeforeOwner, int FactoriesAfterOwner,
        bool InnerPending, bool OuterPending, bool LastExceptionIdentity, bool Faulted, bool Canceled, bool BorrowedIsActualDIInstance,
        int FactoryCalls, int Starts, int Moves, int Reads, int Enumerations = 1);
    private static string CleanupSource() => ListFixture(
        "c.Handlers<CleanupRequest>(DispatchFamily.Stream,HandlerEntry.Session<DIHandler,BorrowedHandlerFactory>());" +
        "c.Pipeline<CleanupRequest>(Stage.Hook<InnerCleanup,InnerFactory>(),Stage.Hook<OuterCleanup,OuterFactory>());") + """
        public sealed record CleanupRequest:IStreamRequest<int>;
        [ZendiatorHandler] public sealed class DIHandler:IStreamRequestHandler<CleanupRequest,int>,IAsyncDisposable {
            private readonly global::Zendiator.Generator.Tests.CanonicalStreamCleanupProbe p;
            public DIHandler(global::Zendiator.Generator.Tests.CanonicalStreamCleanupProbe probe){p=probe;p.RegisterBorrowed(this);}
            public System.Collections.Generic.IAsyncEnumerable<int> HandleAsync(CleanupRequest r,CancellationToken c)=>p.Start(this,r,c);
            public ValueTask DisposeAsync()=>p.DisposeBorrowed(this);
        }
        public sealed class InnerCleanup(global::Zendiator.Generator.Tests.CanonicalStreamCleanupProbe p):IStreamItemMap<CleanupRequest,int>,IAsyncDisposable {
            public int Map(CleanupRequest r,int item,CancellationToken c)=>p.Map("I",r,item,c);
            public ValueTask DisposeAsync()=>p.DisposeOwned("I");
        }
        public sealed class OuterCleanup(global::Zendiator.Generator.Tests.CanonicalStreamCleanupProbe p):IStreamItemMap<CleanupRequest,int>,IAsyncDisposable {
            public int Map(CleanupRequest r,int item,CancellationToken c)=>p.Map("O",r,item,c);
            public ValueTask DisposeAsync()=>p.DisposeOwned("O");
        }
        public sealed class InnerFactory(global::Zendiator.Generator.Tests.CanonicalStreamCleanupProbe p):IOwnedSessionFactory<CleanupRequest,InnerCleanup>,IDisposable {
            public InnerCleanup Create(CleanupRequest r,CancellationToken c){p.Factory("I",r,c);return new(p);}
            public void Dispose()=>p.DisposeFactory("I");
        }
        public sealed class OuterFactory(global::Zendiator.Generator.Tests.CanonicalStreamCleanupProbe p):IOwnedSessionFactory<CleanupRequest,OuterCleanup>,IDisposable {
            public OuterCleanup Create(CleanupRequest r,CancellationToken c){p.Factory("O",r,c);return new(p);}
            public void Dispose()=>p.DisposeFactory("O");
        }
        public sealed class BorrowedHandlerFactory(System.IServiceProvider services,global::Zendiator.Generator.Tests.CanonicalStreamCleanupProbe p):IBorrowedSessionFactory<CleanupRequest,DIHandler>,IDisposable {
            public DIHandler Create(CleanupRequest r,CancellationToken c){p.Factory("H",r,c);return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DIHandler>(services);}
            public void Dispose()=>p.DisposeFactory("H");
        }
        """;
    [Fact]
    public async Task Stream_cleanup_pending_failures_and_DI_owned_borrowed_results_match_reference_without_repeated_release()
    {
        var fixture = CompileCleanup();
        foreach (var profile in new[] { "pending-success", "pending-two-failures" })
        {
            var reference = await RunCleanup(fixture, profile, true); var canonical = await RunCleanup(fixture, profile, false);
            SaveCleanup(profile, new { reference, canonical, productionEnabled = false });
            Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Events, canonical.Events);
            Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events }, canonical);
            Assert.Equal(new[] { 3 }, canonical.Values); Assert.Equal(1, canonical.InnerCalls); Assert.Equal(1, canonical.OuterCalls);
            Assert.Equal(1, canonical.InnerConsumptions); Assert.Equal(1, canonical.OuterConsumptions); Assert.Equal(1, canonical.SourceDisposals);
            Assert.Equal(0, canonical.BorrowedBeforeOwner); Assert.Equal(1, canonical.BorrowedAfterOwner);
            Assert.Equal(0, canonical.FactoriesBeforeOwner); Assert.Equal(3, canonical.FactoriesAfterOwner);
            Assert.True(canonical.InnerPending); Assert.True(canonical.OuterPending); Assert.True(canonical.BorrowedIsActualDIInstance);
            Assert.Equal(profile == "pending-two-failures", canonical.LastExceptionIdentity); Assert.Equal(profile == "pending-two-failures", canonical.Faulted); Assert.False(canonical.Canceled);
        }
    }
    private static CleanupFixture CompileCleanup()
    {
        var source = CleanupSource(); var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "Cleanup_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var graph = Graph(driver); var generated = driver.GetRunResult().Results.Single().GeneratedSources; using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        var folder = CleanupFolder("input");
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
            File.WriteAllText(Path.Combine(folder, "binding.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                emitted.Success,
                graph.IsValid,
                facts = graph.Facts,
                owners = graph.StreamSchedule!.Owners.Where(o => o.Kind == StreamOwnerKind.Session),
                diagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), inSource = d.Location.IsInSource }),
                compiler = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                productionEnabled = false
            }));
        }
        Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id); Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        Assert.Equal(2, graph.StreamSchedule!.Owners.Count(o => o.Kind == StreamOwnerKind.Session && !o.Borrowed));
        Assert.Single(graph.StreamSchedule.Owners, o => o.Kind == StreamOwnerKind.Session && o.Borrowed && o.Cleanup == StreamCleanup.None);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)); Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assert.Single(generated, s => s.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
        bytes.Position = 0; var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var names = new[] { "DIHandler", "InnerCleanup", "OuterCleanup", "BorrowedHandlerFactory", "InnerFactory", "OuterFactory" };
        return new(assembly.GetType("App.CleanupRequest")!, names.ToDictionary(n => n, n => assembly.GetType("App." + n)!),
            Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal)).GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!);
    }
    private static async Task<CleanupResult> RunCleanup(CleanupFixture f, string profile, bool reference)
    {
        var request = Activator.CreateInstance(f.Request)!; var p = new CanonicalStreamCleanupProbe { Request = request }; var services = new ServiceCollection();
        foreach (var (name, role) in new[] { ("DIHandler", "H.Result"), ("BorrowedHandlerFactory", "H.F"), ("InnerFactory", "I.F"), ("OuterFactory", "O.F") })
        {
            var type = f.Types[name]; services.AddScoped(type, sp => { p.Resolve(role); return Activator.CreateInstance(type, name == "BorrowedHandlerFactory" ? new object[] { sp, p } : new object[] { p })!; });
        }
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope(); IAsyncEnumerable<int> stream;
        if (reference)
        {
            var types = new[] { f.Request }.Concat(new[] { "DIHandler", "InnerCleanup", "OuterCleanup", "BorrowedHandlerFactory", "InnerFactory", "OuterFactory" }.Select(n => f.Types[n])).ToArray();
            stream = (IAsyncEnumerable<int>)typeof(CanonicalStreamCleanupReference).GetMethod("Create")!.MakeGenericMethod(types).Invoke(null, [scope.ServiceProvider, request])!;
        }
        else stream = (IAsyncEnumerable<int>)f.Create.Invoke(null, [scope.ServiceProvider, request, default(CancellationToken)])!;
        var cursor = stream.GetAsyncEnumerator(); Assert.Empty(p.Events); Assert.True(await cursor.MoveNextAsync()); var values = new[] { cursor.Current };
        Assert.False(await cursor.MoveNextAsync()); Assert.Equal(0, p.InnerCalls); Assert.Equal(0, p.OuterCalls); Assert.Equal(0, p.BorrowedCalls);
        var disposal = cursor.DisposeAsync().AsTask(); await p.InnerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var innerPending = !disposal.IsCompleted;
        Assert.True(innerPending); Assert.Equal(1, p.SourceDisposals); Assert.Equal(1, p.InnerCalls); Assert.Equal(0, p.OuterCalls); Assert.Equal(0, p.InnerGate.Consumptions); Assert.Equal(0, p.BorrowedCalls); Assert.Equal(0, p.FactoryDisposals);
        var fail = profile == "pending-two-failures"; p.Events.Add(fail ? "Cleanup.Fail:I" : "Cleanup.Release:I");
        if (fail) p.InnerGate.Fail(p.InnerError); else p.InnerGate.Complete(true);
        await p.OuterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var outerPending = !disposal.IsCompleted;
        Assert.True(outerPending); Assert.Equal(1, p.InnerGate.Consumptions); Assert.Equal(1, p.OuterCalls); Assert.Equal(0, p.OuterGate.Consumptions);
        p.Events.Add(fail ? "Cleanup.Fail:O" : "Cleanup.Release:O"); if (fail) p.OuterGate.Fail(p.OuterError); else p.OuterGate.Complete(true);
        Exception? error = null; try { await disposal.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception e) { error = e; }
        if (fail) Assert.Same(p.OuterError, error); else Assert.Null(error); Assert.Equal(fail, disposal.IsFaulted); Assert.False(disposal.IsCanceled);
        Assert.Equal(1, p.InnerGate.Consumptions); Assert.Equal(1, p.OuterGate.Consumptions); var events = p.Events.Count;
        await cursor.DisposeAsync(); Assert.Equal(events, p.Events.Count); Assert.Equal(1, p.InnerCalls); Assert.Equal(1, p.OuterCalls);
        Assert.Equal(1, p.InnerGate.Consumptions); Assert.Equal(1, p.OuterGate.Consumptions);
        var borrowedBefore = p.BorrowedCalls; var factoriesBefore = p.FactoryDisposals;
        var actual = scope.ServiceProvider.GetRequiredService(f.Types["DIHandler"]); Assert.Same(p.BorrowedResult, actual);
        await scope.DisposeAsync(); Assert.Equal(1, p.BorrowedCalls); Assert.Equal(3, p.FactoryDisposals);
        return new(values, [.. p.Events], p.InnerCalls, p.OuterCalls, p.InnerGate.Consumptions, p.OuterGate.Consumptions, p.SourceDisposals,
            borrowedBefore, p.BorrowedCalls, factoriesBefore, p.FactoryDisposals, innerPending, outerPending, ReferenceEquals(error, p.OuterError), disposal.IsFaulted, disposal.IsCanceled,
            ReferenceEquals(actual, p.BorrowedResult), p.FactoryCalls, p.Starts, p.Moves, p.Reads);
    }
    private static string? CleanupFolder(string name)
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "cleanup-borrowed", name); Directory.CreateDirectory(folder); return folder;
    }
    private static void SaveCleanup(string name, object value)
    {
        var folder = CleanupFolder("results"); if (folder != null) File.WriteAllText(Path.Combine(folder, name + ".json"), System.Text.Json.JsonSerializer.Serialize(value));
    }
}
