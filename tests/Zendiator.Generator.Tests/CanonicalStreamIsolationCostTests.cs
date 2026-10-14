using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.Tests.Contracts;
using Xunit;

namespace Zendiator.Generator.Tests;

public sealed partial class GeneratorTests
{
    private sealed record IsolationObservation(string Phase, string? Ambient);
    private sealed record IsolationTrace(string Backend, int[] Values, IsolationObservation[] Events,
        int Starts, int Moves, int Reads, int Disposals, int Effects, int FirstFactories, int LastFactories);

    [Fact]
    public async Task Stream_isolation_one_shape_fixes_migration_trace_before_optional_finite_cost()
    {
        var fixture = FallbackFixtureFor("int");
        var reference = await ObserveIsolation(fixture, "reference");
        var canonical = await ObserveIsolation(fixture, "canonical");
        var fused = await ObserveIsolation(fixture, "fused");
        var isolated = await ObserveIsolation(fixture, "layer-isolation");
        Assert.Equal(reference.Values, canonical.Values); Assert.Equal(reference.Values, fused.Values);
        Assert.Equal(reference.Events, canonical.Events); Assert.Equal(reference.Events, fused.Events);
        Assert.Equal(reference with { Backend = canonical.Backend, Values = canonical.Values, Events = canonical.Events }, canonical);
        Assert.Equal(reference with { Backend = fused.Backend, Values = fused.Values, Events = fused.Events }, fused);
        Assert.Equal(reference.Values, isolated.Values);
        Assert.Equal(reference with { Backend = isolated.Backend, Values = isolated.Values, Events = isolated.Events }, isolated);
        Assert.Equal(new[] { 2 }, reference.Values);
        Assert.Equal(1, reference.Reads); Assert.Equal(2, reference.Moves); Assert.Equal(1, reference.Disposals);
        Assert.Equal(1, reference.Effects); Assert.Equal(1, reference.FirstFactories); Assert.Equal(1, reference.LastFactories);

        // Freeze the differing contract, rather than demanding equality with the new oracle.
        foreach (var result in new[] { reference, canonical, fused })
        {
            Assert.Equal("factory:H", result.Events.First(e => e.Phase == "H").Ambient);
            Assert.Equal("factory:H", result.Events.First(e => e.Phase == "Current").Ambient);
            Assert.Equal("getter", result.Events.First(e => e.Phase == "factory:F1").Ambient);
            Assert.Equal("callback:F1", result.Events.First(e => e.Phase == "factory:Map").Ambient);
            Assert.Equal("callback:Map", result.Events.First(e => e.Phase == "factory:F2").Ambient);
        }
        Assert.Equal("factory:H", isolated.Events.First(e => e.Phase == "H").Ambient);
        Assert.Equal("factory:H", isolated.Events.First(e => e.Phase == "Current").Ambient);
        foreach (var role in new[] { "F1", "Map", "F2" })
        {
            Assert.Equal("caller-first", isolated.Events.First(e => e.Phase == "guard:" + role).Ambient);
            Assert.Equal("caller-first", isolated.Events.First(e => e.Phase == "factory:" + role).Ambient);
            Assert.Equal("factory:" + role, isolated.Events.First(e => e.Phase == role).Ambient);
        }
        Assert.Equal("caller-first", isolated.Events.First(e => e.Phase == "guard:H.start").Ambient);
        Assert.Equal("factory:H", isolated.Events.First(e => e.Phase == "guard:H.move").Ambient);
        Assert.Equal("caller-end", isolated.Events.Last(e => e.Phase == "guard:H.move").Ambient);
        Assert.Equal("caller-end", isolated.Events.Single(e => e.Phase == "cleanup:H").Ambient);
        Assert.Equal("caller-end", isolated.Events.Single(e => e.Phase == "H.finally").Ambient);
        Assert.NotEqual(reference.Events.First(e => e.Phase == "factory:Map").Ambient,
            isolated.Events.First(e => e.Phase == "factory:Map").Ambient);
        var commonPhases = isolated.Events.Where(e => !e.Phase.StartsWith("guard:", StringComparison.Ordinal)
            && e.Phase != "cleanup:H").Select(e => e.Phase);
        Assert.Equal(reference.Events.Select(e => e.Phase), commonPhases);
        Assert.Equal(new IsolationObservation[]
        {
            new("consumer.move.enter", "caller-first"), new("guard:H.start", "caller-first"),
            new("factory:H", "caller-first"), new("H", "factory:H"), new("Get", "factory:H"),
            new("guard:H.move", "factory:H"), new("Move", "factory:H"), new("Current", "factory:H"),
            new("guard:F1", "caller-first"), new("factory:F1", "caller-first"), new("F1", "factory:F1"),
            new("guard:Map", "caller-first"), new("factory:Map", "caller-first"), new("Map", "factory:Map"),
            new("guard:F2", "caller-first"), new("factory:F2", "caller-first"), new("F2", "factory:F2"),
            new("consumer.move.return", "caller-first"), new("consumer.move.completed", "caller-first"),
            new("consumer.current", "caller-first"), new("consumer.move.enter", "caller-end"),
            new("guard:H.move", "caller-end"), new("Move", "caller-end"), new("cleanup:H", "caller-end"),
            new("H.finally", "caller-end"), new("consumer.move.return", "caller-end"),
            new("consumer.move.completed", "caller-end"), new("consumer.dispose.enter", "caller-dispose"),
            new("consumer.dispose.return", "caller-dispose"), new("consumer.dispose.completed", "caller-dispose")
        }, isolated.Events);

        var evidence = Environment.GetEnvironmentVariable("ZENDIATOR_CANONICAL_EVIDENCE_DIR");
        var folder = string.IsNullOrEmpty(evidence) ? null : Path.Combine(evidence, "stream-isolation-cost");
        if (folder != null)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "one-shape.json"), JsonSerializer.Serialize(new
            {
                reference,
                canonical,
                fused,
                isolated,
                contractRelation = "different ambient contracts;isolation reproduction model,not literal 0.4.0",
                candidateOnly = true,
                productionEnabled = false,
                sameContractPerformanceGate = false
            }));
        }
        // Off by default. BOCCHI must approve this fixed, different-contract cost proposal.
        // All trace assertions above must pass before it starts; no extra shape or retry.
        if (Environment.GetEnvironmentVariable("ZENDIATOR_STREAM_ISOLATION_COST") == "1")
        {
            var cost = MeasureIsolationCost();
            if (folder != null) File.WriteAllText(Path.Combine(folder, "finite-cost.json"), JsonSerializer.Serialize(cost));
        }
    }

    private static async Task<IsolationTrace> ObserveIsolation(FallbackFixture f, string backend)
    {
        var p = new CanonicalStreamFallbackProbe { Input = [1] };
        var events = new List<IsolationObservation>(); var request = Activator.CreateInstance(f.Request, [1])!; p.Outer = request;
        var saved = CanonicalStreamFallbackProbe.Ambient.Value; var savedContext = SynchronizationContext.Current;
        void Observe(string phase, object? ignored = null, CancellationToken token = default)
        {
            events.Add(new(phase, CanonicalStreamFallbackProbe.Ambient.Value));
            if (phase.StartsWith("factory:", StringComparison.Ordinal)) CanonicalStreamFallbackProbe.Ambient.Value = phase;
            if (phase == "Current") CanonicalStreamFallbackProbe.Ambient.Value = "getter";
            if (phase is "F1" or "Map" or "F2") CanonicalStreamFallbackProbe.Ambient.Value = "callback:" + phase;
        }
        p.Observe = Observe;
        var collection = new ServiceCollection(); var roles = new[] { "H", "F1", "Map", "F2" };
        for (var i = 0; i < f.Services.Length; i++)
        { var type = f.Services[i]; var role = roles[i]; collection.AddTransient(type, _ => { p.Resolve(role); return Activator.CreateInstance(type, p)!; }); }
        using var provider = collection.BuildServiceProvider();
        try
        {
            SynchronizationContext.SetSynchronizationContext(null); CanonicalStreamFallbackProbe.Ambient.Value = "caller-create";
            IAsyncEnumerable<int> stream;
            if (backend is "reference" or "layer-isolation")
            {
                var bridge = Activator.CreateInstance(f.Bridge, provider)!;
                var start = f.Start.CreateDelegate(typeof(Func<,,>).MakeGenericType(f.Request, typeof(CancellationToken), typeof(IAsyncEnumerable<int>)), bridge);
                stream = backend == "reference"
                    ? (IAsyncEnumerable<int>)typeof(CanonicalStreamItemReference).GetMethod("FilterMapFilter")!
                        .MakeGenericMethod(f.Request, typeof(int), f.Services[1], f.Services[2], f.Services[3])
                        .Invoke(null, [provider, request, start, default(CancellationToken), null])!
                    : (IAsyncEnumerable<int>)typeof(CanonicalStreamIsolationControl).GetMethod("Create")!
                        .MakeGenericMethod(f.Request, f.Services[1], f.Services[2], f.Services[3])
                        .Invoke(null, [provider, request, start, (Action<string>)(phase => Observe(phase))])!;
            }
            else stream = (IAsyncEnumerable<int>)(backend == "fused" ? f.Fused! : f.Canonical)
                .Invoke(null, [provider, request, default(CancellationToken)])!;
            var cursor = stream.GetAsyncEnumerator(); Assert.Empty(events);
            var values = new List<int>();
            for (var i = 0; i < 2; i++)
            {
                var caller = i == 0 ? "caller-first" : "caller-end";
                CanonicalStreamFallbackProbe.Ambient.Value = caller; Observe("consumer.move.enter");
                var move = cursor.MoveNextAsync(); Assert.True(move.IsCompletedSuccessfully);
                Observe("consumer.move.return"); Assert.Equal(caller, CanonicalStreamFallbackProbe.Ambient.Value);
                var ok = await move; Observe("consumer.move.completed"); Assert.Equal(caller, CanonicalStreamFallbackProbe.Ambient.Value);
                Assert.Equal(i == 0, ok);
                if (ok) { var reads = p.Reads; values.Add(cursor.Current); _ = cursor.Current; Assert.Equal(reads, p.Reads); Observe("consumer.current"); }
            }
            CanonicalStreamFallbackProbe.Ambient.Value = "caller-dispose"; Observe("consumer.dispose.enter");
            var dispose = cursor.DisposeAsync(); Assert.True(dispose.IsCompletedSuccessfully);
            Observe("consumer.dispose.return"); Assert.Equal("caller-dispose", CanonicalStreamFallbackProbe.Ambient.Value);
            await dispose; Observe("consumer.dispose.completed"); Assert.Equal("caller-dispose", CanonicalStreamFallbackProbe.Ambient.Value);
            return new(backend, [.. values], [.. events], p.Starts, p.Moves, p.Reads, p.Disposals, p.Effects, p.BeforeResolves, p.AfterResolves);
        }
        finally { CanonicalStreamFallbackProbe.Ambient.Value = saved; SynchronizationContext.SetSynchronizationContext(savedContext); }
    }

    // Fixed local cost material for the same int/reference-request/borrowed-class F1->Map->F2 shape.
    // Tiny handwritten services avoid dynamic-compilation/reflection/trace recording in the timed region.
    // Neither series is a benchmark of generated production code or of historical 0.4.0.
    private sealed class IsolationCostRequest { }
    private sealed class IsolationCostSource : IAsyncEnumerable<int>, IAsyncEnumerator<int>
    {
        private int _index; public int Current { get { CanonicalStreamFallbackProbe.Ambient.Value = "getter"; return 1; } }
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) { _index = 0; return this; }
        public ValueTask<bool> MoveNextAsync() => new(++_index <= 16);
        public ValueTask DisposeAsync() => default;
    }
    private sealed class IsolationCostHandler
    {
        public IAsyncEnumerable<int> Start(IsolationCostRequest request, CancellationToken token) => new IsolationCostSource();
    }
    private sealed class IsolationCostFirst : IStreamItemFilter<IsolationCostRequest, int>
    { public bool Accept(IsolationCostRequest request, int item, CancellationToken token) { CanonicalStreamFallbackProbe.Ambient.Value = "callback:F1"; return item > 0; } }
    private sealed class IsolationCostMap : IStreamItemMap<IsolationCostRequest, int>
    { public int Map(IsolationCostRequest request, int item, CancellationToken token) { CanonicalStreamFallbackProbe.Ambient.Value = "callback:Map"; return item * 2; } }
    private sealed class IsolationCostLast : IStreamItemFilter<IsolationCostRequest, int>
    { public bool Accept(IsolationCostRequest request, int item, CancellationToken token) { CanonicalStreamFallbackProbe.Ambient.Value = "callback:F2"; return item < 4; } }
    private sealed record IsolationCostRow(string Contract, int Round, int Enumerations, int ItemsPerEnumeration,
        int Checksum, long StopwatchTicks, double NanosecondsPerEnumeration, long AllocatedBytes, double BytesPerEnumeration);

    private static object MeasureIsolationCost()
    {
        var saved = CanonicalStreamFallbackProbe.Ambient.Value; var savedContext = SynchronizationContext.Current;
        var services = new ServiceCollection();
        services.AddTransient(_ => { CanonicalStreamFallbackProbe.Ambient.Value = "factory:H"; return new IsolationCostHandler(); });
        services.AddTransient(_ => { CanonicalStreamFallbackProbe.Ambient.Value = "factory:F1"; return new IsolationCostFirst(); });
        services.AddTransient(_ => { CanonicalStreamFallbackProbe.Ambient.Value = "factory:Map"; return new IsolationCostMap(); });
        services.AddTransient(_ => { CanonicalStreamFallbackProbe.Ambient.Value = "factory:F2"; return new IsolationCostLast(); });
        using var provider = services.BuildServiceProvider(); var request = new IsolationCostRequest();
        IAsyncEnumerable<int> Start(IsolationCostRequest r, CancellationToken token) => provider.GetRequiredService<IsolationCostHandler>().Start(r, token);
        Func<IAsyncEnumerable<int>> canonical = () => CanonicalStreamItemReference.FilterMapFilter<IsolationCostRequest, int,
            IsolationCostFirst, IsolationCostMap, IsolationCostLast>(provider, request, Start);
        Func<IAsyncEnumerable<int>> isolated = () => CanonicalStreamIsolationControl.Create<IsolationCostRequest,
            IsolationCostFirst, IsolationCostMap, IsolationCostLast>(provider, request, Start);
        int Consume(Func<IAsyncEnumerable<int>> create)
        {
            CanonicalStreamFallbackProbe.Ambient.Value = "cost-caller";
            var cursor = create().GetAsyncEnumerator(); var checksum = 0; var items = 0;
            while (true)
            {
                var move = cursor.MoveNextAsync();
                if (!move.IsCompletedSuccessfully) throw new InvalidOperationException("Cost fixture must complete synchronously.");
                if (!move.GetAwaiter().GetResult()) break; // Exactly one consumption.
                checksum += cursor.Current; items++;
            }
            var dispose = cursor.DisposeAsync();
            if (!dispose.IsCompletedSuccessfully) throw new InvalidOperationException("Cost cleanup must complete synchronously.");
            dispose.GetAwaiter().GetResult();
            if (checksum != 32 || items != 16 || CanonicalStreamFallbackProbe.Ambient.Value != "cost-caller")
                throw new InvalidOperationException("Cost work/result/caller mismatch.");
            return checksum;
        }
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            for (var i = 0; i < 64; i++) { Consume(canonical); Consume(isolated); }
            var rows = new List<IsolationCostRow>(); var deadline = Stopwatch.StartNew();
            for (var round = 0; round < 4; round++)
            {
                foreach (var isolatedFirst in new[] { round % 2 == 0, round % 2 != 0 })
                {
                    var create = isolatedFirst ? isolated : canonical;
                    if (deadline.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Finite cost budget expired.");
                    var startBytes = GC.GetAllocatedBytesForCurrentThread(); var startTicks = Stopwatch.GetTimestamp(); var checksum = 0;
                    for (var i = 0; i < 512; i++) checksum += Consume(create);
                    var ticks = Stopwatch.GetTimestamp() - startTicks; var bytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
                    if (deadline.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Finite cost budget expired.");
                    if (checksum != 16384) throw new InvalidOperationException("Batch checksum mismatch.");
                    rows.Add(new(isolatedFirst ? "per-layer-isolation" : "new-single-orchestration-reference", round, 512, 16,
                        checksum, ticks, ticks * (1_000_000_000d / Stopwatch.Frequency) / 512, bytes, bytes / 512d));
                }
            }
            return new
            {
                rows,
                warmupEnumerationsPerContract = 64,
                measuredEnumerationsPerContract = 2048,
                stopwatchFrequency = Stopwatch.Frequency,
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                runtimeVersion = Environment.Version.ToString(),
                sdk = "record external dotnet --info",
                traceRecording = false,
                allocationScope = "current thread;all completions asserted synchronous;DI/services/source/enumerators included",
                interpretation = "different contracts;small local isolation reproduction cost,no throughput/adoption/baseline claim"
            };
        }
        finally { CanonicalStreamFallbackProbe.Ambient.Value = saved; SynchronizationContext.SetSynchronizationContext(savedContext); }
    }
}
