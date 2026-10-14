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
    private static readonly string[] AsyncItemProfiles = ["completed", "pending-filter", "pending-map", "filter-drop", "failure-cleanup", "cancel-map"];
    private sealed record AsyncItemFixture(Type Request, Type[] Services, Type Bridge, MethodInfo Start, MethodInfo Canonical);
    private sealed record AsyncItemResult(int[] Values, StreamSeedEvent[] Events, bool InitiallyCompleted, bool Canceled, bool Faulted,
        string Error, int Starts, int Moves, int Reads, int Disposals, int LastCalls, int FirstConsumptions, int MapConsumptions, int DisposeConsumptions);

    [Fact]
    public async Task Stream_async_item_canonical_completed_pending_and_terminal_contracts_match_reference_once()
    {
        var fixture = CompileAsyncItemFixture(); // Exactly one consumer compilation for all six profiles.
        foreach (var profile in AsyncItemProfiles)
        {
            var reference = await RunAsyncItem(fixture, "reference", profile);
            var canonical = await RunAsyncItem(fixture, "canonical", profile);
            WriteAsyncItemArtifact(profile + ".json", new { profile, reference, canonical, productionEnabled = false });
            Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Events, canonical.Events);
            Assert.Equal(reference with { Values = canonical.Values, Events = canonical.Events }, canonical);
            Assert.Equal(1, canonical.Starts); Assert.Equal(1, canonical.Reads); Assert.Equal(1, canonical.Disposals);
            Assert.Equal(1, canonical.FirstConsumptions);
            Assert.Equal(profile == "filter-drop" ? 0 : 1, canonical.MapConsumptions);
            Assert.Equal(profile == "failure-cleanup" ? 1 : 0, canonical.DisposeConsumptions);
            Assert.Equal(profile == "completed", canonical.InitiallyCompleted);
            Assert.Equal(profile == "cancel-map", canonical.Canceled); Assert.Equal(profile == "failure-cleanup", canonical.Faulted);
            Assert.Equal(profile is "completed" or "pending-filter" ? new[] { 2 } : [], canonical.Values);
            Assert.Equal(profile is "completed" or "pending-filter" or "pending-map" ? 1 : 0, canonical.LastCalls);
            Assert.Equal((profile is "filter-drop" or "pending-map") || canonical.Values.Length != 0 ? 2 : 1, canonical.Moves);
            if (profile == "pending-map") Assert.Contains(canonical.Events, e => e.Stage == "F2" && e.Input == 4 && e.Result == false);
            Assert.DoesNotContain(canonical.Events, e => e.Kind == "Resolve" && e.Stage == "F2" && canonical.LastCalls == 0);
        }
    }

    private static string AsyncItemFixtureSource()
    {
        return StreamGraphFixture()
            .Replace("public sealed class ItemsHandler :", "public sealed class ItemsHandler(global::Zendiator.Generator.Tests.CanonicalStreamAsyncItemProbe p) :")
            .Replace("HandleAsync(Items r,CancellationToken c)=>throw new NotImplementedException();", "HandleAsync(Items r,CancellationToken c)=>p.Start(r,c);")
            .Replace("public sealed class Positive : IStreamItemFilter<Items,int>", "public sealed class Positive(global::Zendiator.Generator.Tests.CanonicalStreamAsyncItemProbe p) : IAsyncStreamItemFilter<Items,int>")
            .Replace("public bool Accept(Items r,int item,CancellationToken c)=>item>0;", "public ValueTask<bool> AcceptAsync(Items r,int item,CancellationToken c)=>p.First(r,item,c);")
            .Replace("public sealed class DoubleItem : IStreamItemMap<Items,int>", "public sealed class DoubleItem(global::Zendiator.Generator.Tests.CanonicalStreamAsyncItemProbe p) : IAsyncStreamItemMap<Items,int>")
            .Replace("public int Map(Items r,int item,CancellationToken c)=>item*2;", "public ValueTask<int> MapAsync(Items r,int item,CancellationToken c)=>p.Map(r,item,c);")
            .Replace("public sealed class Small :", "public sealed class Small(global::Zendiator.Generator.Tests.CanonicalStreamAsyncItemProbe p) :")
            .Replace("=>item<4;", "=>p.Last(r,item,c);") + """
            public sealed class AsyncItemBridge(System.IServiceProvider services) {
                public System.Collections.Generic.IAsyncEnumerable<int> Start(Items r,CancellationToken t)=>
                    Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ItemsHandler>(services).HandleAsync(r,t);
            }
            """;
    }

    private static AsyncItemFixture CompileAsyncItemFixture()
    {
        var source = AsyncItemFixtureSource();
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "AsyncItem_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        Assert.Empty(diagnostics); // Public registration is accepted; this existing test still exercises the internal/reference pair.
        var graph = Graph(driver); Assert.True(graph.IsValid, string.Join(",", graph.Facts.Select(f => f.Reason)));
        var schedule = Assert.IsType<CanonicalStreamExecutionSchedule>(graph.StreamSchedule);
        var hookAwaits = schedule.Awaits.Where(a => a.Producer.Phase is StreamPhase.Filter or StreamPhase.Map).ToArray();
        Assert.Equal(2, hookAwaits.Length); Assert.Equal(new[] { 0, 1 }, hookAwaits.Select(a => a.Producer.Stage).OrderBy(i => i));
        Assert.All(hookAwaits, a => { Assert.True(a.ConsumeOnce); Assert.True(a.ConfigureAwaitFalse); Assert.Contains("cursor/item", a.RetainedSlots); });
        var generated = driver.GetRunResult().Results.Single().GeneratedSources;
        Assert.Single(generated, f => f.HintName.StartsWith("Zendiator.StreamCanonical.", StringComparison.Ordinal));
        Assert.DoesNotContain(generated, f => f.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        WriteAsyncItemArtifact("compile.json", new { emitted.Success, consumerCompilations = 1, productionEnabled = false,
            diagnostics = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }) });
        var folder = AsyncItemEvidenceFolder();
        if (folder != null)
        {
            File.WriteAllText(Path.Combine(folder, "input.cs"), source); File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            foreach (var file in generated) File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes); var bridge = assembly.GetType("App.AsyncItemBridge")!;
        return new(assembly.GetType("App.Items")!, new[] { "ItemsHandler", "Positive", "DoubleItem", "Small" }.Select(n => assembly.GetType("App." + n)!).ToArray(),
            bridge, bridge.GetMethod("Start")!, Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamCanonical_", StringComparison.Ordinal))
                .GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!);
    }

    private static async Task<AsyncItemResult> RunAsyncItem(AsyncItemFixture fixture, string backend, string profile)
    {
        using var api = new CancellationTokenSource(); var request = Activator.CreateInstance(fixture.Request)!;
        var events = new StreamSeedEvents(request, api.Token);
        var probe = new CanonicalStreamAsyncItemProbe { Profile = profile, Request = request, Token = api.Token, Events = events };
        var collection = new ServiceCollection(); var roles = new[] { "H", "F1", "Map", "F2" };
        for (var i = 0; i < fixture.Services.Length; i++)
        { var type = fixture.Services[i]; var role = roles[i]; collection.AddTransient(type, _ => { probe.Resolve(role); return Activator.CreateInstance(type, probe)!; }); }
        using var provider = collection.BuildServiceProvider(); IAsyncEnumerable<int> stream;
        if (backend == "reference")
        {
            var bridge = Activator.CreateInstance(fixture.Bridge, provider)!;
            var start = fixture.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(fixture.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<int>)), bridge);
            stream = (IAsyncEnumerable<int>)typeof(CanonicalStreamAsyncItemReference).GetMethod("FilterMapFilter")!
                .MakeGenericMethod(fixture.Request, typeof(int), fixture.Services[1], fixture.Services[2], fixture.Services[3])
                .Invoke(null, [provider, request, start, api.Token])!;
        }
        else stream = (IAsyncEnumerable<int>)fixture.Canonical.Invoke(null, [provider, request, api.Token])!;
        var cursor = stream.GetAsyncEnumerator(); var values = new List<int>(); Exception? error = null;
        Assert.Empty(events.Snapshot()); Assert.Equal(0, probe.Starts);
        var operation = cursor.MoveNextAsync(); var initiallyCompleted = operation.IsCompleted; var move = operation.AsTask();
        if (profile != "completed")
        {
            await probe.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(move.IsCompleted);
            Assert.Equal(1, probe.Reads); Assert.Equal(0, probe.LastCalls);
            Assert.Equal(probe.FirstPending ? 0 : 1, probe.FirstGate.Consumptions); Assert.Equal(0, probe.MapGate.Consumptions);
            if (probe.FirstPending) Assert.DoesNotContain(events.Snapshot(), e => e.Stage == "Map");
            if (backend == "canonical")
            { Assert.Throws<InvalidOperationException>(() => cursor.Current); Assert.Throws<InvalidOperationException>(() => cursor.MoveNextAsync()); Assert.Throws<InvalidOperationException>(() => cursor.DisposeAsync()); }
            if (profile == "cancel-map") { api.Cancel(); probe.Event("Cancel", "Consumer"); }
            if (probe.FirstPending) { probe.Event("Release", "F1", result: profile != "filter-drop"); probe.FirstGate.Complete(profile != "filter-drop"); }
            else if (profile == "failure-cleanup") { probe.Event("Throw", "Map", error: probe.HookError); probe.MapGate.Fail(probe.HookError); }
            else { var mapped = profile == "pending-map" ? 4 : 2; probe.Event("Release", "Map", output: mapped); probe.MapGate.Complete(mapped); }
        }
        if (profile == "failure-cleanup")
        {
            await probe.Closing.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(move.IsCompleted); Assert.Equal(1, probe.MapGate.Consumptions);
            Assert.Equal(0, probe.DisposeGate.Consumptions); probe.Event("InnerDisposeThrow", "H", error: probe.CleanupError); probe.DisposeGate.Fail(probe.CleanupError);
        }
        try
        {
            if (await move.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                values.Add(cursor.Current); var reads = probe.Reads; _ = cursor.Current; Assert.Equal(reads, probe.Reads);
                probe.Event("ConsumerYield", "Consumer", output: values[0]);
                Assert.False(await cursor.MoveNextAsync());
            }
        }
        catch (Exception caught) { error = caught; probe.Event("ConsumerCatch", "Consumer", error: caught); }
        if (profile == "failure-cleanup") Assert.Same(probe.CleanupError, error);
        else if (profile == "cancel-map") { var canceled = Assert.IsAssignableFrom<OperationCanceledException>(error); Assert.Equal(api.Token, canceled.CancellationToken); }
        else Assert.Null(error);
        await cursor.DisposeAsync(); await cursor.DisposeAsync(); // Source detached: no repeated cleanup after failure/end.
        if (backend == "canonical") { Assert.False(await cursor.MoveNextAsync()); Assert.Throws<InvalidOperationException>(() => cursor.Current); }
        return new([.. values], events.Snapshot(), initiallyCompleted, move.IsCanceled, move.IsFaulted,
            error == null ? "none" : ReferenceEquals(error, probe.CleanupError) ? "cleanup" : error is OperationCanceledException ? "cancel" : "unexpected",
            probe.Starts, probe.Moves, probe.Reads, probe.Disposals, probe.LastCalls, probe.FirstGate.Consumptions, probe.MapGate.Consumptions, probe.DisposeGate.Consumptions);
    }
    private static string? AsyncItemEvidenceFolder()
    {
        var root = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR"); if (string.IsNullOrEmpty(root)) return null;
        var folder = Path.Combine(root, "stream-async-item"); Directory.CreateDirectory(folder); return folder;
    }
    private static void WriteAsyncItemArtifact(string name, object value)
    { var folder = AsyncItemEvidenceFolder(); if (folder != null) File.WriteAllText(Path.Combine(folder, name), System.Text.Json.JsonSerializer.Serialize(value)); }
}
