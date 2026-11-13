using System.Text.Json;
using Competitive.ShortCircuit;
using Evidence = Competitive.FoundatioRelease.FoundatioRuntimeEvidence;

namespace Competitive;

internal static class BenchmarkGate
{
    internal const string Suite = "short";
    internal const int ExpectedCases = 6;

    internal static async Task Run(string output)
    {
        if (ConstructionLog.Instrumented)
            throw new InvalidOperationException("Timing-binary semantics require an uninstrumented assembly");
        var details = await SemanticGate().ConfigureAwait(false);
        var records = JsonSerializer.SerializeToElement(details).GetProperty("semanticRecords").EnumerateArray().ToArray();
        if (records.Length != ExpectedCases || records.Select(row => row.GetProperty("actualType").GetString()).Distinct().Count() != ExpectedCases)
            throw new InvalidOperationException("Expected all six original short-circuit semantic records");
        Directory.CreateDirectory(output);
        ArtifactEvidence.Write(Path.Combine(output, "semantic-details.json"), details);
        ArtifactEvidence.Write(Path.Combine(output, "correctness.json"), records.Select(row => new
        {
            status = "Passed",
            suite = Suite,
            library = row.GetProperty("peer").GetString(),
            type = row.GetProperty("actualType").GetString(),
            method = row.GetProperty("method").GetString()
        }).ToArray());
    }

    private static async Task<object> SemanticGate()
    {
        var rows = new List<object>();
        foreach (var peer in ShortRegistration.Peers)
        {
            using var benchmark = Create(peer);
            await benchmark.Setup().ConfigureAwait(false);
            var afterWarm = benchmark.ConstructionSnapshot();
            using var live = new CancellationTokenSource();
            var liveProbe = new ShortProbe(live.Token);
            var liveRequest = new ShortRequest(41, liveProbe);
            var liveValue = await benchmark.GateDispatch(liveRequest, live.Token).ConfigureAwait(false);
            CheckNormal(liveValue, liveProbe);

            var noneProbe = new ShortProbe(CancellationToken.None);
            var noneValue = await benchmark.GateDispatch(new ShortRequest(41, noneProbe), CancellationToken.None).ConfigureAwait(false);
            CheckNormal(noneValue, noneProbe);

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            var canceledProbe = new ShortProbe(canceled.Token);
            bool sawCancellation = false;
            try
            {
                await benchmark.GateDispatch(new ShortRequest(41, canceledProbe), canceled.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException error)
            {
                if (error.CancellationToken != canceled.Token)
                    throw new InvalidOperationException("Cancellation token changed");
                sawCancellation = true;
            }
            if (!sawCancellation || canceledProbe.Events.Count != 0 || canceledProbe.HandlerCalls != 0)
                throw new InvalidOperationException("Canceled request entered work or completed successfully");

            rows.Add(new
            {
                caseId = "ShortCircuit/" + peer + "/WarmDispatch",
                peer,
                actualType = benchmark.GetType().FullName,
                benchmarkType = benchmark.GetType().FullName,
                method = "WarmDispatch",
                parameters = new Dictionary<string, object?>(),
                benchmarkMethod = "WarmDispatch",
                dispatchMethod = "Dispatch",
                entryType = ShortRegistration.EntryType(peer).FullName,
                normalLive = new
                {
                    result = liveValue,
                    liveProbe.Events,
                    liveProbe.HandlerCalls,
                    liveProbe.TokenChecks
                },
                normalNone = new
                {
                    result = noneValue,
                    noneProbe.Events,
                    noneProbe.HandlerCalls,
                    noneProbe.TokenChecks
                },
                canceled = new
                {
                    sawCancellation,
                    canceledProbe.Events,
                    canceledProbe.HandlerCalls
                },
                afterWarm,
                afterChecks = benchmark.ConstructionSnapshot(),
                registration = ((ShortState)benchmark).RegistrationDescriptors
            });
        }
        return new
        {
            schemaVersion = 1,
            phase = ConstructionLog.Instrumented ? "constructor-diagnostics-only" : "new-semantic-gate",
            passed = true,
            newSemanticGate = !ConstructionLog.Instrumented,
            shortConstructionDiagnosticsEnabled = ConstructionLog.Instrumented,
            runtime = "10.0.14",
            di = "10.0.12",
            coveredCaseIds = ShortRegistration.Peers.Select(peer => "ShortCircuit/" + peer + "/WarmDispatch").ToArray(),
            semanticRecords = rows,
            binding = Evidence.Capture("semantic-post-run-no-timing")
        };
    }

    private static IShortBenchmark Create(string peer) => peer switch
    {
        "Zendiator" => new ZendiatorShortCircuit(),
        "MediatR" => new MediatRShortCircuit(),
        "Mediator" => new MediatorShortCircuit(),
        "DispatchR" => new DispatchRShortCircuit(),
        "Immediate" => new ImmediateShortCircuit(),
        "Foundatio" => new FoundatioShortCircuit(),
        _ => throw new ArgumentOutOfRangeException(nameof(peer))
    };

    private static void CheckNormal(int value, ShortProbe probe)
    {
        if (value != 42 || !probe.Events.SequenceEqual(new[] { 1 }) || probe.HandlerCalls != 0 || probe.TokenChecks != 1)
            throw new InvalidOperationException("Expected outer Enter1 then normal 42 with no inner/handler work");
    }
}
