using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Zendiator;

namespace Competitive;

internal static class StructUpstreamGate
{
    internal static async Task Run(string output)
    {
        var ordinary0 = new ZendiatorStream0();
        var opt0 = new ZendiatorStructUpstreamStream0();
        var ordinary5 = new ZendiatorStream5();
        var opt5 = new ZendiatorStructUpstreamStream5();
        var ordinaryRelay = new ZendiatorRelay5();
        var optRelay = new ZendiatorStructUpstreamRelay5();
        ordinary0.Setup();
        opt0.Setup();
        ordinary5.Setup();
        opt5.Setup();
        ordinaryRelay.Setup();
        optRelay.Setup();
        var rows = new List<object>();
        try
        {
            foreach (var count in new[] { 0, 1, 16, 1024 })
                foreach (var asynchronous in new[] { false, true })
                    foreach (var early in new[] { false, true })
                    {
                        await Pair("Stream0", count, asynchronous, early,
                            (p, t) => ordinary0.Invoke(new(count, asynchronous, p), t),
                            (p, t) => opt0.Invoke(new(count, asynchronous, p), t),
                            (p, sink, t) => ordinary0.InvokeTyped(new(count, asynchronous, p), sink, t),
                            (p, sink, t) => opt0.InvokeTyped(new(count, asynchronous, p), sink, t));
                        await Pair("Stream5", count, asynchronous, early,
                            (p, t) => ordinary5.Invoke(new(count, asynchronous, p), t),
                            (p, t) => opt5.Invoke(new(count, asynchronous, p), t),
                            (p, sink, t) => ordinary5.InvokeTyped(new(count, asynchronous, p), sink, t),
                            (p, sink, t) => opt5.InvokeTyped(new(count, asynchronous, p), sink, t));
                        await Pair("Relay5", count, asynchronous, early,
                            (p, t) => ordinaryRelay.Invoke(new(count, asynchronous, p), t),
                            (p, t) => optRelay.Invoke(new(count, asynchronous, p), t),
                            (p, sink, t) => ordinaryRelay.InvokeTyped(new(count, asynchronous, p), sink, t),
                            (p, sink, t) => optRelay.InvokeTyped(new(count, asynchronous, p), sink, t));
                    }
        }
        finally
        {
            ordinary0.Cleanup();
            opt0.Cleanup();
            ordinary5.Cleanup();
            opt5.Cleanup();
            ordinaryRelay.Cleanup();
            optRelay.Cleanup();
        }
        Directory.CreateDirectory(output);
        var library = typeof(InterleavedOrderer).GetMethod("Library", BindingFlags.NonPublic | BindingFlags.Static)!;
        var driverRows = new List<object>();
        foreach (var type in new[] { typeof(ZendiatorStructUpstreamStream0), typeof(ZendiatorStructUpstreamStream5), typeof(ZendiatorStructUpstreamRelay5) })
        {
            var cases = BenchmarkDotNet.Running.BenchmarkConverter.TypeToBenchmarks(type, BenchmarkDotNet.Configs.DefaultConfig.Instance).BenchmarksCases;
            var names = cases.Select(c => (string)library.Invoke(null, new object[] { c })!).Distinct().ToArray();
            if (names.Length != 1 || names[0] != "Zendiator" || new InterleavedOrderer().GetExecutionOrder(cases.ToImmutableArray()).Count() != cases.Length)
                throw new InvalidOperationException("Struct upstream C# driver classification failed.");
            driverRows.Add(new
            {
                Type = type.Name,
                Library = names[0],
                Cases = cases.Length,
                Scope = "Discovery/order only; no benchmark executed"
            });
        }
        var unknownCases = BenchmarkDotNet.Running.BenchmarkConverter.TypeToBenchmarks(
            typeof(NativePoolingBenchmarks), BenchmarkDotNet.Configs.DefaultConfig.Instance).BenchmarksCases;
        var unknownOrder = new InterleavedOrderer().GetExecutionOrder(unknownCases.ToImmutableArray()).ToArray();
        if (unknownOrder.Length != unknownCases.Length || !unknownOrder.ToHashSet().SetEquals(unknownCases))
            throw new InvalidOperationException("Unprefixed C# benchmark cases were lost or duplicated.");
        await File.WriteAllTextAsync(Path.Combine(output, "struct-upstream-driver-identification.json"), JsonSerializer.Serialize(driverRows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"STRUCT_UPSTREAM_PARITY_OK: {rows.Count} cases; all three C# benchmark types classified as Zendiator");
        await File.WriteAllTextAsync(Path.Combine(output, "struct-upstream-correctness.json"),
            JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));

        async Task Pair(string name, int count, bool asynchronous, bool early,
            Func<Probe, CancellationToken, IAsyncEnumerable<int>> ordinary,
            Func<Probe, CancellationToken, IAsyncEnumerable<int>> opt,
            Func<Probe, GateSink, CancellationToken, ValueTask> ordinaryTyped,
            Func<Probe, GateSink, CancellationToken, ValueTask> optTyped)
        {
            using var token = new CancellationTokenSource();
            foreach (var typed in new[] { false, true })
            {
                var expected = new Probe();
                var actual = new Probe();
                var expectedSink = new GateSink(early);
                var actualSink = new GateSink(early);
                if (typed)
                {
                    await ordinaryTyped(expected, expectedSink, token.Token).ConfigureAwait(false);
                    await optTyped(actual, actualSink, token.Token).ConfigureAwait(false);
                }
                else
                {
                    await Pull(ordinary(expected, token.Token), expectedSink).ConfigureAwait(false);
                    await Pull(opt(actual, token.Token), actualSink).ConfigureAwait(false);
                }
                if (!expectedSink.Values.SequenceEqual(actualSink.Values) || !expected.Events.SequenceEqual(actual.Events) ||
                    expected.Handlers != actual.Handlers || expected.Items != actual.Items || expected.Disposals != actual.Disposals ||
                    actual.Handlers != 1 || actual.Disposals != 1 || actual.Items != (early ? Math.Min(1, count) : count))
                    throw new InvalidOperationException($"Struct upstream parity failed: {name}/{count}/{asynchronous}/{early}/{typed}");
                rows.Add(new
                {
                    name,
                    count,
                    asynchronous,
                    early,
                    typed,
                    status = "Passed",
                    actual.Handlers,
                    actual.Items,
                    actual.Disposals
                });
            }
        }
    }


    // Setup/cleanup-only child evidence. This is not a formal measurement artifact proof.
    internal static void RecordChild(string scenario, string output)
    {
        Environment.SetEnvironmentVariable("COLD_CAPTURE_CHILD", "1");
        Environment.SetEnvironmentVariable("COLD_EVIDENCE_DIR", output);
        Environment.SetEnvironmentVariable("COLD_EXPECT_Z_SHA", ArtifactEvidence.Hash(typeof(Zendiator.DependencyInjection.ZendiatorServiceResolver).Assembly.Location));
        switch (scenario)
        {
            case nameof(ZendiatorStructUpstreamStream0):
                var zero = new ZendiatorStructUpstreamStream0();
                zero.Setup();
                zero.Cleanup();
                break;
            case nameof(ZendiatorStructUpstreamStream5):
                var five = new ZendiatorStructUpstreamStream5();
                five.Setup();
                five.Cleanup();
                break;
            case nameof(ZendiatorStructUpstreamRelay5):
                var relay = new ZendiatorStructUpstreamRelay5();
                relay.Setup();
                relay.Cleanup();
                break;
            default:
                throw new ArgumentException("Unknown struct upstream benchmark type.", nameof(scenario));
        }
        Console.WriteLine("STRUCT_UPSTREAM_CHILD_SETUP_OK: " + scenario);
    }

    private static async Task Pull(IAsyncEnumerable<int> source, GateSink sink)
    {
        await foreach (var item in source.ConfigureAwait(false))
            if (!await sink.OnNextAsync(item, default).ConfigureAwait(false))
                break;
    }

    private sealed class GateSink(bool early) : IStreamSink<int>
    {
        internal readonly List<int> Values = new();
        public ValueTask<bool> OnNextAsync(int item, CancellationToken token)
        {
            Values.Add(item);
            return new(!early);
        }
    }
}
