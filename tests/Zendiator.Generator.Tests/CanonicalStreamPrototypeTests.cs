using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.SourceGenerator;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

// User-controlled inputs/callbacks shared by the generated candidate and accepted reference.
public sealed class CanonicalStreamPrototypeProbe
{
    public static readonly AsyncLocal<string?> Ambient = new();
    public int[] Input = [-1, 1, 2];
    public readonly List<string> Trace = [];
    public int Moves, Reads, Disposals, Effects;
    public Exception? MapError, CleanupError;
    public CancellationTokenSource? CancelInFirst;
    public Func<ValueTask<bool>>? PendingMove;
    public Func<ValueTask>? PendingDispose;
    public bool Context;
    public Action<string, object?, CancellationToken>? Observe;
    public Exception? GetEnumeratorError;
    public object? Request;
    public CancellationToken StartToken, EnumeratorToken;
    public void Resolve(string role)
    {
        Trace.Add("resolve:" + role);
        Observe?.Invoke("factory:" + role + ".enter", null, default);
        if (Context && role == "F1") Ambient.Value = "factory";
        if (Context && role == "Map") Trace.Add("Map.factory@" + Ambient.Value);
        Observe?.Invoke("factory:" + role + ".exit", null, default);
    }
    public bool First(object request, int item, CancellationToken token)
    {
        Observe?.Invoke("F1.enter", request, token);
        Trace.Add("F1:" + item);
        if (Context) { Trace.Add("F1@" + Ambient.Value); Ambient.Value = "callback"; }
        if (CancelInFirst is not null) { CancelInFirst.Cancel(); Observe?.Invoke("F1.exit", request, token); return false; }
        Observe?.Invoke("F1.exit", request, token); return item > 0;
    }
    public int Map(object request, int item, CancellationToken token)
    {
        Observe?.Invoke("Map.enter", request, token);
        Trace.Add("Map:" + item); Effects++;
        if (Context) Trace.Add("Map@" + Ambient.Value);
        if (MapError is not null) throw MapError;
        Observe?.Invoke("Map.exit", request, token); return item * 2;
    }
    public bool Last(object request, int item, CancellationToken token)
    { Observe?.Invoke("F2.enter", request, token); Trace.Add("F2:" + item); Observe?.Invoke("F2.exit", request, token); return item < 4; }
    public IAsyncEnumerable<int> Start(object request, CancellationToken token)
    {
        Assert.Same(Request, request); StartToken = token; Observe?.Invoke("handler.enter", request, token);
        Trace.Add("H.start"); var source = new Source(this); Observe?.Invoke("handler.exit", request, token); return source;
    }
    private sealed class Source(CanonicalStreamPrototypeProbe probe) : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index = -1;
        public int Current
        {
            get
            {
                probe.Observe?.Invoke("Current.enter", probe.Request, probe.EnumeratorToken); probe.Reads++;
                var item = probe.Input[_index]; probe.Observe?.Invoke("Current.exit", probe.Request, probe.EnumeratorToken); return item;
            }
        }
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        {
            probe.EnumeratorToken = token; probe.Observe?.Invoke("GetEnumerator.enter", probe.Request, token);
            if (probe.GetEnumeratorError is not null) throw probe.GetEnumeratorError;
            probe.Observe?.Invoke("GetEnumerator.exit", probe.Request, token); return this;
        }
        public ValueTask<bool> MoveNextAsync()
        {
            probe.Observe?.Invoke("Move.enter", probe.Request, probe.EnumeratorToken); probe.Moves++; _index++;
            var pending = _index == 0 && probe.PendingMove is not null ? probe.PendingMove() : new(_index < probe.Input.Length);
            probe.Observe?.Invoke("Move.return", probe.Request, probe.EnumeratorToken); return pending;
        }
        public ValueTask DisposeAsync()
        {
            probe.Disposals++; probe.Trace.Add("H.finally");
            probe.Observe?.Invoke("Dispose.enter", probe.Request, probe.EnumeratorToken);
            var pending = probe.CleanupError is not null ? ValueTask.FromException(probe.CleanupError) : probe.PendingDispose?.Invoke() ?? default;
            probe.Observe?.Invoke("Dispose.return", probe.Request, probe.EnumeratorToken); return pending;
        }
    }
}

public sealed partial class GeneratorTests
{
    private sealed record StreamPrototypeFixture(Assembly Assembly, Type Request, Type[] Services, MethodInfo Create, MethodInfo Start,
        Type Bridge, string GeneratedSource, CanonicalStreamPrototypeModel Projection, CanonicalRouteGraph RouteGraph);
    private static readonly Lazy<StreamPrototypeFixture> StreamPrototype = new(CompileStreamPrototype);
    private static StreamPrototypeFixture CompileStreamPrototype()
    {
        var source = StreamGraphFixture()
            .Replace("public sealed class ItemsHandler : IStreamRequestHandler<Items,int> {", "public sealed class ItemsHandler : IStreamRequestHandler<Items,int> { private readonly global::Zendiator.Generator.Tests.CanonicalStreamPrototypeProbe p; public ItemsHandler(global::Zendiator.Generator.Tests.CanonicalStreamPrototypeProbe p){this.p=p;}")
            .Replace("HandleAsync(Items r,CancellationToken c)=>throw new NotImplementedException();", "HandleAsync(Items r,CancellationToken c)=>p.Start(r,c);")
            .Replace("public sealed class Positive : IStreamItemFilter<Items,int> {", "public sealed class Positive : IStreamItemFilter<Items,int> { private readonly global::Zendiator.Generator.Tests.CanonicalStreamPrototypeProbe p; public Positive(global::Zendiator.Generator.Tests.CanonicalStreamPrototypeProbe p){this.p=p;}")
            .Replace("Accept(Items r,int item,CancellationToken c)=>item>0;", "Accept(Items r,int item,CancellationToken c)=>p.First(r,item,c);")
            .Replace("public sealed class Small : IStreamItemFilter<Items,int> {", "public sealed class Small : IStreamItemFilter<Items,int> { private readonly global::Zendiator.Generator.Tests.CanonicalStreamPrototypeProbe p; public Small(global::Zendiator.Generator.Tests.CanonicalStreamPrototypeProbe p){this.p=p;}")
            .Replace("Accept(Items r,int item,CancellationToken c)=>item<4;", "Accept(Items r,int item,CancellationToken c)=>p.Last(r,item,c);")
            .Replace("public sealed class DoubleItem : IStreamItemMap<Items,int> {", "public sealed class DoubleItem : IStreamItemMap<Items,int> { private readonly global::Zendiator.Generator.Tests.CanonicalStreamPrototypeProbe p; public DoubleItem(global::Zendiator.Generator.Tests.CanonicalStreamPrototypeProbe p){this.p=p;}")
            .Replace("Map(Items r,int item,CancellationToken c)=>item*2;", "Map(Items r,int item,CancellationToken c)=>p.Map(r,item,c);") + """
            public sealed class StreamPrototypeBridge(System.IServiceProvider services) {
                public System.Collections.Generic.IAsyncEnumerable<int> Start(Items r,CancellationToken t)=>
                    Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ItemsHandler>(services).HandleAsync(r,t);
            }
            """;
        var driver = SyncDriver().RunGeneratorsAndUpdateCompilation(SyncCompilation(source, "StreamPrototype_" + Guid.NewGuid().ToString("N")), out var output, out var diagnostics);
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "stream-prototype");
        if (folder != null)
        {
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "input.cs"), source);
            foreach (var file in driver.GetRunResult().Results.Single().GeneratedSources)
                File.WriteAllText(Path.Combine(folder, file.HintName), file.SourceText.ToString());
        }
        Assert.Equal("ZEN0037", Assert.Single(diagnostics).Id); // Candidate only, no public runtime acceptance.
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var generated = Assert.Single(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
        var projection = Assert.IsType<CanonicalStreamPrototypeModel>(Assert.Single(driver.GetRunResult().Results.Single()
            .TrackedSteps["CanonicalStreamPrototypeProjection"].SelectMany(s => s.Outputs)).Value);
        var routeGraph = Assert.IsType<CanonicalRouteGraph>(Assert.Single(driver.GetRunResult().Results.Single()
            .TrackedSteps["CanonicalRouteGraphEntry"].SelectMany(s => s.Outputs)).Value);
        using var bytes = new MemoryStream(); var emitted = output.Emit(bytes);
        if (folder != null)
        {
            File.WriteAllBytes(Path.Combine(folder, "generated.dll"), bytes.ToArray());
            File.WriteAllText(Path.Combine(folder, "compile.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                emitted.Success, candidateOnly = true, productionEnabled = false,
                compilerDiagnostics = emitted.Diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() }),
                generatorDiagnostics = diagnostics.Select(d => new { d.Id, severity = d.Severity.ToString(), message = d.GetMessage() })
            }));
        }
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics)); bytes.Position = 0;
        var assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
        var prototype = Assert.Single(assembly.GetTypes(), t => t.Name.StartsWith("__ZendiatorStreamPrototype_", StringComparison.Ordinal));
        var bridge = assembly.GetType("App.StreamPrototypeBridge")!;
        return new(assembly, assembly.GetType("App.Items")!, new[] { "ItemsHandler", "Positive", "DoubleItem", "Small" }.Select(n => assembly.GetType("App." + n)!).ToArray(),
            prototype.GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static)!, bridge.GetMethod("Start")!, bridge, generated.SourceText.ToString(), projection, routeGraph);
    }
    private static (ServiceProvider Services, IAsyncEnumerator<int> Cursor) PrototypeCursor(CanonicalStreamPrototypeProbe probe, bool generated,
        CancellationToken token = default, CancellationToken enumeration = default)
    {
        var fixture = StreamPrototype.Value; var collection = new ServiceCollection();
        var roles = new[] { "H", "F1", "Map", "F2" };
        for (var i = 0; i < fixture.Services.Length; i++)
        { var type = fixture.Services[i]; var role = roles[i]; collection.AddTransient(type, _ => { probe.Resolve(role); return Activator.CreateInstance(type, probe)!; }); }
        var provider = collection.BuildServiceProvider(); var request = Activator.CreateInstance(fixture.Request)!;
        probe.Request = request;
        IAsyncEnumerable<int> stream;
        if (generated) stream = (IAsyncEnumerable<int>)fixture.Create.Invoke(null, [provider, request, token])!;
        else
        {
            var bridge = Activator.CreateInstance(fixture.Bridge, provider)!;
            var start = fixture.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(fixture.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<int>)), bridge);
            var method = typeof(CanonicalStreamItemReference).GetMethod("FilterMapFilter")!.MakeGenericMethod(fixture.Request, typeof(int), fixture.Services[1], fixture.Services[2], fixture.Services[3]);
            stream = (IAsyncEnumerable<int>)method.Invoke(null, [provider, request, start, token, null])!;
        }
        return (provider, stream.GetAsyncEnumerator(enumeration));
    }

    [Fact]
    public void Stream_candidate_uses_existing_graph_one_loop_and_never_accepts_wrapper_runtime()
    {
        var fixture = StreamPrototype.Value;
        Assert.Equal(new StreamSiteId(StreamProgram.Dispose, StreamPhase.DetachSource), fixture.Projection.TerminalCleanupEntry);
        Assert.All(fixture.Projection.Schedule.Awaits, a => { Assert.True(a.ConsumeOnce); Assert.True(a.ConfigureAwaitFalse); });
        Assert.Equal(new[] { BoundCallbackPhase.Filter, BoundCallbackPhase.Map, BoundCallbackPhase.Filter }, fixture.Projection.Hooks.Select(h => h.Phase));
        Assert.Equal(1, fixture.GeneratedSource.Split("while (true)", StringSplitOptions.None).Length - 1);
        Assert.Contains("await source.MoveNextAsync().ConfigureAwait(false)", fixture.GeneratedSource);
        Assert.Contains("await detached.DisposeAsync().ConfigureAwait(false)", fixture.GeneratedSource);
        var driver = BindLists(StreamGraphFixture("Stage.Hook<Positive>(),Stage.Wrapper<StreamOpaque>(),Stage.Hook<Small>()"), "ZEN0037");
        Assert.NotNull(Graph(driver).StreamWrapperSchedule);
        Assert.DoesNotContain(driver.GetRunResult().Results.Single().GeneratedSources, s => s.HintName.StartsWith("Zendiator.StreamPrototype.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("normal")] [InlineData("drop")] [InlineData("mask")] [InlineData("cancel")] [InlineData("context")]
    public async Task Stream_generated_candidate_matches_selected_reference_observations(string profile)
    {
        var observations = new List<(int[] Values, string[] Trace, int Moves, int Reads, int Disposals, int Effects, bool Canceled, bool Faulted)>();
        foreach (var generated in new[] { false, true })
        {
            var before = CanonicalStreamPrototypeProbe.Ambient.Value; CanonicalStreamPrototypeProbe.Ambient.Value = "caller";
            using var cancellation = new CancellationTokenSource();
            var map = new FormatException("map"); var cleanup = new InvalidOperationException("cleanup");
            var probe = new CanonicalStreamPrototypeProbe { Input = profile == "drop" ? [-2, -1] : [-1, 1, 2], Context = profile == "context",
                MapError = profile == "mask" ? map : null, CleanupError = profile == "mask" ? cleanup : null, CancelInFirst = profile == "cancel" ? cancellation : null };
            var (services, cursor) = PrototypeCursor(probe, generated, cancellation.Token); using var provider = services;
            var values = new List<int>(); bool canceled = false, faulted = false;
            try
            {
                while (true)
                {
                    var move = cursor.MoveNextAsync(); var outcome = await ContractAwait.Observe(() => move);
                    Assert.Equal("caller", CanonicalStreamPrototypeProbe.Ambient.Value);
                    if (outcome.Error is not null)
                    {
                        canceled = outcome.Canceled; faulted = outcome.Faulted;
                        if (profile == "mask") Assert.Same(cleanup, outcome.Error);
                        else Assert.Equal(cancellation.Token, Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error).CancellationToken);
                        break;
                    }
                    if (!outcome.Value) break; values.Add(cursor.Current); Assert.Equal(values[^1], cursor.Current);
                }
                await cursor.DisposeAsync(); Assert.Equal(1, probe.Disposals);
                observations.Add(([.. values], [.. probe.Trace], probe.Moves, probe.Reads, probe.Disposals, probe.Effects, canceled, faulted));
            }
            finally { CanonicalStreamPrototypeProbe.Ambient.Value = before; }
        }
        var expected = observations[0]; var actual = observations[1];
        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(evidence))
            File.WriteAllText(Path.Combine(evidence, "stream-prototype", "observations-" + profile + ".json"),
                System.Text.Json.JsonSerializer.Serialize(new { profile, candidateOnly = true, productionEnabled = false,
                    reference = new { expected.Values, expected.Trace, expected.Moves, expected.Reads, expected.Disposals, expected.Effects, expected.Canceled, expected.Faulted },
                    candidate = new { actual.Values, actual.Trace, actual.Moves, actual.Reads, actual.Disposals, actual.Effects, actual.Canceled, actual.Faulted } }));
        Assert.Equal(expected.Values, actual.Values); Assert.Equal(expected.Trace, actual.Trace);
        Assert.Equal((expected.Moves, expected.Reads, expected.Disposals, expected.Effects, expected.Canceled, expected.Faulted),
            (actual.Moves, actual.Reads, actual.Disposals, actual.Effects, actual.Canceled, actual.Faulted));
        Assert.Equal(profile is "drop" or "mask" or "cancel" ? Array.Empty<int>() : new[] { 2 }, actual.Values);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stream_generated_candidate_matches_real_pending_Move_or_Dispose_single_consumption(bool disposing)
    {
        foreach (var generated in new[] { false, true })
        {
            var gate = new ContractOnce<bool>(); var probe = new CanonicalStreamPrototypeProbe { Input = [1] };
            if (disposing) probe.PendingDispose = () => gate.Void; else probe.PendingMove = () => gate.Task;
            var (services, cursor) = PrototypeCursor(probe, generated); using var provider = services;
            if (disposing) Assert.True(await cursor.MoveNextAsync());
            if (disposing)
            {
                var pending = cursor.DisposeAsync(); Assert.False(pending.IsCompleted); Assert.Equal(0, gate.Consumptions);
                var completion = pending.AsTask(); gate.Complete(true); await completion.WaitAsync(TimeSpan.FromSeconds(5));
            }
            else
            {
                var pending = cursor.MoveNextAsync(); Assert.False(pending.IsCompleted); Assert.Equal(0, probe.Reads); Assert.Equal(0, gate.Consumptions);
                var completion = pending.AsTask(); gate.Complete(true); Assert.True(await completion.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            Assert.Equal(1, gate.Consumptions); await cursor.DisposeAsync(); Assert.Equal(1, probe.Disposals);
        }
    }
}
