using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;
using Competitive;

// Attributes the bytes of one stream enumeration to object types by sampling GC allocation ticks.
internal static class StreamAllocationProfile
{
    private sealed class AllocationListener : EventListener
    {
        public readonly ConcurrentDictionary<string, int> Samples = new(StringComparer.Ordinal);

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true || e.PayloadNames is null) return;
            var index = e.PayloadNames.IndexOf("TypeName");
            var name = index >= 0 ? e.Payload?[index] as string ?? "?" : "?";
            Samples.AddOrUpdate(name, 1, static (_, count) => count + 1);
        }
    }

    public static async Task Run(string output)
    {
        var cases = new List<(string Name, int Count, bool Asynchronous, Func<ValueTask<int>> Full, Action Cleanup)>();
        void Add<T>(string name, Func<int, bool, T> create, Func<T, ValueTask<int>> full, Action<T> cleanup)
        {
            foreach (var count in new[] { 16, 1024 })
                foreach (var asynchronous in new[] { false, true })
                {
                    var target = create(count, asynchronous);
                    cases.Add((name, count, asynchronous, () => full(target), () => cleanup(target)));
                }
        }
        static T Ready<T>(T target, Action<T> setup) { setup(target); return target; }
        Add("ZendiatorStream0", (n, a) => Ready(new ZendiatorStream0 { Count = n, Asynchronous = a }, x => x.Setup()), x => x.Full(), x => x.Cleanup());
        Add("ImmediateStream0", (n, a) => Ready(new ImmediateStream0 { Count = n, Asynchronous = a }, x => x.Setup()), x => x.Full(), x => x.Cleanup());
        Add("ZendiatorStream5", (n, a) => Ready(new ZendiatorStream5 { Count = n, Asynchronous = a }, x => x.Setup()), x => x.Full(), x => x.Cleanup());
        Add("ImmediateStream5", (n, a) => Ready(new ImmediateStream5 { Count = n, Asynchronous = a }, x => x.Setup()), x => x.Full(), x => x.Cleanup());
        Add("ZendiatorRelay5", (n, a) => Ready(new ZendiatorRelay5 { Count = n, Asynchronous = a }, x => x.Setup()), x => x.Full(), x => x.Cleanup());
        Add("ImmediateRelay5", (n, a) => Ready(new ImmediateRelay5 { Count = n, Asynchronous = a }, x => x.Setup()), x => x.Full(), x => x.Cleanup());
        var results = new List<object>();
        try
        {
            foreach (var c in cases)
            {
                for (var i = 0; i < 2000; i++) await c.Full();
                var operations = c.Count == 16 ? (c.Asynchronous ? 50_000 : 400_000) : (c.Asynchronous ? 3_000 : 20_000);
                var before = GC.GetTotalAllocatedBytes(precise: true);
                var started = Stopwatch.GetTimestamp();
                for (var i = 0; i < operations; i++) await c.Full();
                var elapsed = Stopwatch.GetElapsedTime(started);
                var bytesPerOperation = (GC.GetTotalAllocatedBytes(precise: true) - before) / (double)operations;
                Dictionary<string, int> samples;
                using (var listener = new AllocationListener())
                {
                    for (var i = 0; i < operations; i++) await c.Full();
                    await Task.Delay(1500);
                    samples = new Dictionary<string, int>(listener.Samples, StringComparer.Ordinal);
                }
                var total = samples.Values.Sum();
                var types = samples.OrderByDescending(static pair => pair.Value)
                    .Select(pair => new { type = pair.Key, samples = pair.Value, share = (double)pair.Value / total, estimatedBytesPerOperation = bytesPerOperation * pair.Value / total })
                    .ToArray();
                results.Add(new { c.Name, c.Count, c.Asynchronous, operations, bytesPerOperation, nanosecondsPerOperation = elapsed.TotalNanoseconds / operations, totalSamples = total, types });
                Console.WriteLine($"{c.Name} Count={c.Count} Async={c.Asynchronous}: {bytesPerOperation:F1} B/op, {total} samples");
                foreach (var type in types.Where(static t => t.share >= 0.02))
                    Console.WriteLine($"    {type.share,7:P1} ~{type.estimatedBytesPerOperation,7:F1} B  {type.type}");
            }
        }
        finally
        {
            foreach (var c in cases) c.Cleanup();
        }
        var path = Path.Combine(output, "stream-allocations.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(path);
    }
}
