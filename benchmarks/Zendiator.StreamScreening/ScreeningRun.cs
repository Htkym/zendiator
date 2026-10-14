using System.Diagnostics;
using System.Text.Json;

namespace Zendiator.StreamScreening;

public readonly record struct Outcome(long Checksum, long OutputCount);
public sealed record Raw(string Shape, int Launch, string Backend, string Order, string Pass, int Batch,
    int Enumerations, long? ElapsedTicks, long? AllocatedBefore, long? AllocatedAfter,
    double? NsPerEnumeration, double? BytesPerEnumeration, Outcome Output, int Gen0, int Gen1, int Gen2);

public static class ScreeningRun
{
    public const int Warmup = 2048, BatchSize = 512, TimingPairs = 8, AllocationPairs = 4;
    public static Action<Raw>? RawSink { get; set; }
    private static bool _helpersInitialized;

    // Zero enumerations: initialize both nullable Raw schemas through the actual
    // serializer/writer sink, before preflight and workload warmup. The host
    // moves these dummy bytes to a separate initialization file before Run.
    public static void InitializeMeasurementHelpers()
    {
        var save = RawSink ?? throw new InvalidOperationException("Raw sink must be installed before initialization.");
        var frequency = Stopwatch.Frequency;
        var start = Stopwatch.GetTimestamp();
        var ticks = Stopwatch.GetTimestamp() - start;
        var g0 = GC.CollectionCount(0); var g1 = GC.CollectionCount(1); var g2 = GC.CollectionCount(2);
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        save(new Raw("initialization", 0, "init", "none", "initialize-timing", -1, 0,
            ticks, null, null, ticks * 1_000_000_000.0 / frequency, null, new(0, 0), g0, g1, g2));
        save(new Raw("initialization", 0, "init", "none", "initialize-allocation", -1, 0,
            null, allocatedBefore, allocatedAfter, null, allocatedAfter - allocatedBefore, new(0, 0), g0, g1, g2));
        _helpersInitialized = true;
    }

    // Every delegate calls a fully typed, directly compiled consumer. Reflection
    // binds Entry.Run only once in the host, before this method is entered.
    public static async Task<string> Run(string shape, int launch, long expected,
        Func<int, Task<Outcome>> a, Func<int, Task<Outcome>> b, Action drainA, Action drainB,
        object preflight)
    {
        if (!_helpersInitialized) throw new InvalidOperationException("Measurement helpers have not been initialized.");
        var rows = new Raw[2 * (TimingPairs + AllocationPairs)];
        var slot = 0;
        var firstA = launch != 2;
        foreach (var reference in firstA ? new[] { true, false } : new[] { false, true })
        {
            var result = await (reference ? a : b)(Warmup).ConfigureAwait(false);
            (reference ? drainA : drainB)();
            Check(result, Warmup, expected);
        }

        for (var pass = 0; pass < 2; pass++)
        {
            var count = pass == 0 ? TimingPairs : AllocationPairs;
            for (var pair = 0; pair < count; pair++)
            {
                var ab = (pair % 2 == 0) == firstA;
                for (var side = 0; side < 2; side++)
                {
                    var reference = (side == 0) == ab;
                    var consume = reference ? a : b;
                    var drain = reference ? drainA : drainB;
                    drain();
                    var g0 = GC.CollectionCount(0); var g1 = GC.CollectionCount(1); var g2 = GC.CollectionCount(2);
                    long? allocatedBefore = pass == 1 ? GC.GetTotalAllocatedBytes(precise: true) : null;
                    var start = pass == 0 ? Stopwatch.GetTimestamp() : 0;
                    var result = await consume(BatchSize).ConfigureAwait(false);
                    drain(); // Includes worker completion before either interval ends.
                    long? ticks = pass == 0 ? Stopwatch.GetTimestamp() - start : null;
                    long? allocatedAfter = pass == 1 ? GC.GetTotalAllocatedBytes(precise: true) : null;
                    Check(result, BatchSize, expected);
                    var row = new Raw(shape, launch, reference ? "A" : "B", ab ? "AB" : "BA",
                        pass == 0 ? "timing" : "allocation", pair, BatchSize, ticks, allocatedBefore, allocatedAfter,
                        ticks is {} elapsed ? elapsed * 1_000_000_000.0 / Stopwatch.Frequency / BatchSize : null,
                        allocatedAfter is {} end && allocatedBefore is {} begin ? (end - begin) / (double)BatchSize : null,
                        result, GC.CollectionCount(0) - g0, GC.CollectionCount(1) - g1, GC.CollectionCount(2) - g2);
                    rows[slot++] = row;
                    // Endpoint exclusion does not eliminate effects on the next
                    // block's JIT/GC/cache state. Fixed variance gates still apply.
                    RawSink?.Invoke(row);
                }
            }
        }
        drainA(); drainB();
        return JsonSerializer.Serialize(new
        {
            shape, launch, inputLength = 1, productionEnabled = false, preflight, rows,
            stopwatchFrequency = Stopwatch.Frequency,
            measurementHelpersInitialized = true,
            comparisonScope = shape is "completed" or "suspended"
                ? "concrete independent reference including per-enumeration handler-start bridge delegate;whole difference is not attributed solely to generated loop/body"
                : "concrete independent reference;whole enumeration including scope and ownership work",
            interBatchSaveEffects = "synchronous saving outside endpoints can affect later block JIT/GC/cache state;fixed variance gates apply;no automatic remeasurement",
            allocationScope = "entire process managed allocation including both callback work and completion worker;not generated-code-only;native excluded",
            workerWorkDrained = true
        });
    }

    private static void Check(Outcome result, int count, long value)
    {
        if (result.OutputCount != count || result.Checksum != value * count)
            throw new InvalidOperationException("Output count/checksum mismatch;stop without retry.");
    }
}
