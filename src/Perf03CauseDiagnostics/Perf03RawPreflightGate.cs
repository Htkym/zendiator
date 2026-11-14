using System.Security.Cryptography;
using System.Text.Json;
using Competitive.Perf03.Counted;
using ProtocolGate = Competitive.Perf03.Diagnostics.Perf03ProtocolGate;
using NativeEntry = Competitive.Generated.Perf03NamedStreams.Zendiator;

namespace Competitive;

internal sealed record Perf03RawPreflightIdentity(string EvidenceSha256, string MapSha256, int SiteCount);

internal static class Perf03RawPreflightGate
{
    internal const string Protocol = "zendiator-perf03-raw-preflight-v1";
    internal const string EvidencePath = "perf03-cause-raw/raw-preflight.json";
    private const int Count = 1024;

    internal static async Task<Perf03RawPreflightIdentity> Run(string outputDirectory,
        string implementationSha256, string siteMapPath)
    {
        Perf03.Diagnostics.Perf03CauseVariant.RequireBoundCountedEvidence();
        Perf03CountTrace.CheckMap(siteMapPath);
        var rejection = new Perf03CountTrace();
        bool unmappedSiteRejected = rejection.CheckRejection();
        var actual = new List<object>();
        foreach (bool controlled in new[] { false, true })
            foreach (bool relay in new[] { false, true })
                actual.Add(await ObserveWork(relay, controlled).ConfigureAwait(false));
        object protocols = await ProtocolGate.Run().ConfigureAwait(false);
        var evidence = new
        {
            schemaVersion = 1,
            protocol = Protocol,
            implementationSha256,
            variant = Perf03.Diagnostics.Perf03CauseVariant.Id,
            siteMapSha256 = Perf03CountSites.MapSha256,
            compiledSiteCount = Perf03CountSites.Sites.Length,
            unmappedSiteRejected,
            mismatchedKindRejected = unmappedSiteRejected,
            actualWorkCases = actual,
            deterministicProtocols = protocols,
            countedCopyPerturbsScheduling = true,
            timedPendingFractionKnown = false,
            nsPerPendingClaim = false,
            counterSynchronizationExcludedFromAtomicCounts = true,
            deterministicProtocolsValidateCountedTraceAttribution = false,
            controlledWorkValidatesCountedTraceAttribution = true,
            countedWorkFaultOrCancelConsumptionClaim = false,
            countedWorkConsumptionScope = "Successful Work results only; throwing GetResult attempts are not counted by the copied-route success markers",
            scope = "Separate counted Work copies and explicitly substituted deterministic protocol sources; no timed pending denominator"
        };
        string path = Path.Combine(outputDirectory, EvidencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence,
            new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        Console.WriteLine("PERF03_RAW_PREFLIGHT_OK: 4 counted Work cases and 24 substituted protocol cases; no benchmark or timed pending fraction");
        return Check(outputDirectory, implementationSha256, siteMapPath);
    }

    private static async Task<object> ObserveWork(bool relay, bool controlled)
    {
        using var session = new Perf03CauseSession(false, !relay, relay, Count, true);
        var trace = new Perf03CountTrace();
        var probe = new Probe();
        object request = session.Request(probe);
        var services = (NativeEntry)session.Entry;
        IAsyncEnumerator<int> cursor;
        using (trace.Operation(-1, "acquisition"))
        {
            IAsyncEnumerable<int> sequence = relay
                ? __ZendiatorStreamCanonical_B80A3F15A7016AF93086943F607702393A9513056C3C751D172215C32167550B.Create(services, (Relay5)request, session.ApiToken)
                : __ZendiatorStreamCanonical_DF7872ACF770B8DDDA6D6A61054B21A2251A08597B558387231219CCDDFB0B4B.Create(services, (Stream0)request, session.ApiToken);
            cursor = sequence.GetAsyncEnumerator();
        }
        Require(probe.Handlers == 0, "Counted source acquisition invoked the producer.");
        var pump = controlled ? new Perf03YieldPump() : null;
        var held = new List<object>();
        object? reentryProof = null;
        ValueTask<bool>? following = null;
        int followingIndex = -1;
        bool eofSynchronous = false;
        int items = 0, sum = 0, moves = 0, consumptions = 0, consumerIncomplete = 0;
        try
        {
            while (true)
            {
                int index = following is not null ? followingIndex : moves;
                var result = following is { } saved ? saved : Issue(index);
                following = null;
                using var operation = trace.Operation(index, Phase(index));
                if (controlled && index == 1)
                {
                    var awaiter = result.ConfigureAwait(false).GetAwaiter();
                    var reentered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    bool pumping = false, inline = false;
                    int pumpThread = Environment.CurrentManagedThreadId;
                    awaiter.UnsafeOnCompleted(() =>
                    {
                        try
                        {
                            inline = pumping && pumpThread == Environment.CurrentManagedThreadId;
                            Require(Perf03CountTrace.Capture()?.Operation == 1,
                                "The counted consuming continuation lost its old operation ticket.");
                            using var old = trace.Operation(1, "repeated");
                            Require(awaiter.GetResult(), "Counted reentry lost the old item.");
                            consumptions++;
                            ReadItem();
                            followingIndex = moves;
                            following = Issue(followingIndex);
                            reentered.SetResult();
                        }
                        catch (Exception error)
                        {
                            reentered.SetException(error);
                        }
                    });
                    pumping = true;
                    try
                    {
                        pump!.RunOne();
                    }
                    finally { pumping = false; }
                    Require(inline && reentered.Task.IsCompleted, "Counted publication did not reenter inline.");
                    await reentered.Task.ConfigureAwait(false);
                    Require(followingIndex == 2 && following is not null && pump!.PendingCount == 1,
                        "Counted reentry did not hold the next raw operation.");
                    var observations = trace.Peek();
                    bool rootOwned = Perf03.Diagnostics.Perf03CauseVariant.Id == "VRoot" || Perf03.Diagnostics.Perf03CauseVariant.Id == "VWord";
                    var retired = observations.Single(e => e.Operation == 1 && (rootOwned
                        ? e.Kind == "completion.retire" : e.Site == "CS.L0574.atomic"));
                    var published = observations.Single(e => e.Operation == 1 && e.Kind == "completion.publish.result" && e.Owner == retired.Owner);
                    var consumed = observations.Single(e => e.Operation == 1 && e.Owner == retired.Owner && (rootOwned
                        ? e.Kind == "completion.consume" : e.Site == "CS.L0589.atomic"));
                    var nextPull = observations.Single(e => e.Operation == 2 && e.Kind == "input.raw.pull");
                    Require(retired.Owner is not null && published.Sequence < consumed.Sequence &&
                        consumed.Sequence < nextPull.Sequence && nextPull.Sequence < retired.Sequence &&
                        retired.Phase == "repeated", "Counted old publication retirement lost its owner or operation attribution.");
                    reentryProof = new
                    {
                        inline = true,
                        oldOperation = 1,
                        newOperation = 2,
                        oldOwner = retired.Owner,
                        oldPublicationSequence = published.Sequence,
                        oldConsumptionSequence = consumed.Sequence,
                        newRawPullSequence = nextPull.Sequence,
                        oldRetirementSequence = retired.Sequence,
                        oldRetirementSite = retired.Site,
                        oldRetirementPhase = retired.Phase
                    };
                    continue;
                }
                if (controlled && index < Count)
                    pump!.RunOne();
                bool moved = await result.ConfigureAwait(false);
                consumptions++;
                if (!moved)
                    break;
                ReadItem();
            }
        }
        finally
        {
            using var cleanup = trace.Operation(Count + 1, "cleanup");
            await cursor.DisposeAsync().ConfigureAwait(false);
        }
        if (controlled)
            Require(pump!.PostedCount == Count && pump.ExecutedCount == Count && pump.PendingCount == 0 &&
                held.Count == Count && eofSynchronous && reentryProof is not null,
                "Controlled Work scheduling did not hold exactly the declared item operations.");

        ValueTask<bool> Issue(int index)
        {
            Require(index == moves && index <= Count, "Counted source operation order changed.");
            using var operation = trace.Operation(index, Phase(index));
            ValueTask<bool> result;
            long beforePosts = pump?.PostedCount ?? 0;
            if (pump is null)
                result = cursor.MoveNextAsync();
            else
                using (pump.Enter())
                    result = cursor.MoveNextAsync();
            moves++;
            if (!result.IsCompleted)
                consumerIncomplete++;
            if (pump is not null && index < Count)
            {
                var seen = trace.Peek().Where(e => e.Operation == index).ToArray();
                int pulls = seen.Count(e => e.Kind == "input.raw.pull");
                int consumed = seen.Count(e => e.Kind.StartsWith("input.raw.consume.", StringComparison.Ordinal));
                int registrations = seen.Count(e => e.Kind == "raw.register");
                int implicitBegins = seen.Count(e => e.Kind == "input.raw.await.implicit.begin");
                int incompleteRechecks = seen.Count(e => e.Kind == "input.raw.awaiter.IsCompleted" && e.Observed == false);
                Require(!result.IsCompleted && pump.PendingCount == 1 && pump.PostedCount == beforePosts + 1 &&
                    pulls == 1 && consumed == 0 && (index == 0 ? implicitBegins == 1 : registrations == 1 && incompleteRechecks == 1),
                    "A held Work yield did not establish the expected unconsumed raw pending boundary.");
                held.Add(new
                {
                    operation = index,
                    pendingAtReturn = true,
                    queuedCallbacks = 1,
                    rawPulls = pulls,
                    rawConsumptionsBeforePump = consumed,
                    rawRegistrations = registrations,
                    implicitCompilerAwaitBegins = implicitBegins,
                    incompleteAwaiterRechecks = incompleteRechecks
                });
            }
            else if (pump is not null)
            {
                eofSynchronous = result.IsCompleted && pump.PendingCount == 0 && pump.PostedCount == beforePosts;
                Require(eofSynchronous, "Controlled Work EOF unexpectedly posted or remained pending.");
            }
            return result;
        }

        void ReadItem()
        {
            Require(cursor.Current == items, "Counted Work sequence changed.");
            sum += cursor.Current;
            items++;
        }

        static string Phase(int index) => index == 0 ? "first" : index == Count ? "eof" : "repeated";
        await trace.QuiesceAsync().ConfigureAwait(false);
        var snapshot = trace.Snapshot();
        var events = snapshot.Events;
        Require(items == Count && sum == 523776 && moves == Count + 1 && consumptions == moves,
            "Counted Work full-consumption result changed.");
        int[] expectedOrder = relay ? [1, 2, 3, 4, 5, 100] : [100];
        Require(probe.Handlers == 1 && probe.Items == Count && probe.Disposals == 1 &&
            probe.Events.SequenceEqual(expectedOrder), "Counted Work callbacks or cleanup changed.");
        foreach (int operation in Enumerable.Range(0, Count + 1))
        {
            var row = events.Where(e => e.Operation == operation).ToArray();
            Require(row.Count(e => e.Kind == "input.raw.pull") == 1 &&
                row.Count(e => e.Kind.StartsWith("input.raw.consume.", StringComparison.Ordinal)) == 1,
                "A counted raw Work result was lost or consumed more than once.");
        }
        Require(events.Count(e => e.Kind == "input.raw.pull") == Count + 1 &&
            events.Count(e => e.Kind.StartsWith("input.raw.consume.", StringComparison.Ordinal)) == Count + 1,
            "Counted raw Work operations escaped the declared host phases.");
        var phases = new[] { "acquisition", "first", "repeated", "eof", "cleanup" }.Select(phase => new
        {
            phase,
            rawPulls = events.Count(e => e.Phase == phase && e.Kind == "input.raw.pull"),
            explicitRawConsumptions = events.Count(e => e.Phase == phase && e.Kind == "input.raw.consume.explicit"),
            implicitCompilerAwaitConsumptions = events.Count(e => e.Phase == phase && e.Kind == "input.raw.consume.implicit")
        }).ToArray();
        var betweenChecks = new List<object>();
        foreach (int operation in Enumerable.Range(0, Count + 1))
        {
            var row = events.Where(e => e.Operation == operation).ToArray();
            var initial = row.FirstOrDefault(e => e.Kind == "input.raw.IsCompletedSuccessfully" && e.Observed == false);
            var recheck = initial is null ? null : row.FirstOrDefault(e => e.Sequence > initial.Sequence &&
                e.Kind == "input.raw.awaiter.IsCompleted" && e.Observed == true);
            if (initial is not null && recheck is not null)
                betweenChecks.Add(new
                {
                    operation,
                    initialSite = initial.Site,
                    recheckSite = recheck.Site,
                    observation = "success-fast-check false, subsequent awaiter completed; successful Work result consumed once"
                });
        }
        return new
        {
            @case = new
            {
                Type = relay ? "ZendiatorPerf03NamedDefaultRelay5" : "ZendiatorPerf03NamedLiveStream0",
                Method = "FullDiagnostic",
                Count,
                Asynchronous = true
            },
            status = "Passed",
            controlledScheduling = controlled,
            controlledYield = !controlled ? null : new
            {
                postedCallbacks = pump!.PostedCount,
                executedCallbacks = pump.ExecutedCount,
                heldItemOperations = held,
                synchronousEof = eofSynchronous,
                reentry = reentryProof
            },
            countedTraceAttributionValidated = controlled,
            producer = "WorkStreamCursor",
            producerSubstitution = false,
            typedNativeDi = true,
            items,
            sum,
            handlers = probe.Handlers,
            disposals = probe.Disposals,
            callbackOrder = probe.Events.ToArray(),
            rawPulls = Count + 1,
            rawConsumptions = Count + 1,
            rawConsumptionPerOperationVerified = true,
            firstMoveCompilerAwaitInstrumentedAtSourceBoundary = true,
            compilerGeneratedGetResultInstructionCountClaim = false,
            consumerObserved = new
            {
                moveCalls = moves,
                consumptions,
                incompleteAtReturn = consumerIncomplete
            },
            phaseCounts = phases,
            completedBetweenChecks = betweenChecks,
            siteCounts = events.GroupBy(e => (e.Phase, e.Site, e.Kind)).Select(group => new
            {
                phase = group.Key.Phase,
                site = group.Key.Site,
                kind = group.Key.Kind,
                calls = group.Count(),
                trueObservations = group.Count(e => e.Observed == true),
                falseObservations = group.Count(e => e.Observed == false)
            }).ToArray(),
            ownerTypes = snapshot.Owners,
            trace = events,
            countedCopyPerturbsScheduling = true,
            timedPendingFractionKnown = false
        };
    }

    internal static Perf03RawPreflightIdentity Check(string outputDirectory, string implementationSha256,
        string siteMapPath)
    {
        Perf03CountTrace.CheckMap(siteMapPath);
        string directory = Path.Combine(outputDirectory, "perf03-cause-raw");
        string path = Path.Combine(outputDirectory, EvidencePath);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            Directory.EnumerateFileSystemEntries(directory).Count() != 1 ||
            (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidOperationException("Raw preflight evidence is missing or substituted.");
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var evidence = document.RootElement;
        Require(evidence.GetProperty("schemaVersion").GetInt32() == 1 &&
            evidence.GetProperty("protocol").GetString() == Protocol &&
            evidence.GetProperty("implementationSha256").GetString() == implementationSha256 &&
            evidence.GetProperty("variant").GetString() == Perf03.Diagnostics.Perf03CauseVariant.Id &&
            evidence.GetProperty("siteMapSha256").GetString() == Perf03CountSites.MapSha256 &&
            evidence.GetProperty("compiledSiteCount").GetInt32() == Perf03CountSites.Sites.Length &&
            evidence.GetProperty("unmappedSiteRejected").GetBoolean() &&
            evidence.GetProperty("mismatchedKindRejected").GetBoolean() &&
            evidence.GetProperty("countedCopyPerturbsScheduling").GetBoolean() &&
            !evidence.GetProperty("timedPendingFractionKnown").GetBoolean() &&
            !evidence.GetProperty("nsPerPendingClaim").GetBoolean() &&
            evidence.GetProperty("counterSynchronizationExcludedFromAtomicCounts").GetBoolean() &&
            !evidence.GetProperty("deterministicProtocolsValidateCountedTraceAttribution").GetBoolean() &&
            evidence.GetProperty("controlledWorkValidatesCountedTraceAttribution").GetBoolean() &&
            !evidence.GetProperty("countedWorkFaultOrCancelConsumptionClaim").GetBoolean() &&
            evidence.GetProperty("countedWorkConsumptionScope").GetString() ==
                "Successful Work results only; throwing GetResult attempts are not counted by the copied-route success markers",
            "Raw preflight identity or limitations changed.");
        var cases = evidence.GetProperty("actualWorkCases");
        Require(cases.GetArrayLength() == 4, "Raw preflight Work coverage changed.");
        var expected = new HashSet<(string Type, bool Controlled)>();
        foreach (var type in new[] { "ZendiatorPerf03NamedLiveStream0", "ZendiatorPerf03NamedDefaultRelay5" })
            foreach (bool controlled in new[] { false, true })
                expected.Add((type, controlled));
        foreach (var row in cases.EnumerateArray())
            Require(expected.Remove((row.GetProperty("case").GetProperty("Type").GetString()!,
                    row.GetProperty("controlledScheduling").GetBoolean())) &&
                row.GetProperty("status").GetString() == "Passed" && row.GetProperty("rawPulls").GetInt32() == Count + 1 &&
                row.GetProperty("rawConsumptions").GetInt32() == Count + 1 &&
                row.GetProperty("rawConsumptionPerOperationVerified").GetBoolean() &&
                row.GetProperty("countedTraceAttributionValidated").GetBoolean() == row.GetProperty("controlledScheduling").GetBoolean() &&
                !row.GetProperty("producerSubstitution").GetBoolean() && row.GetProperty("typedNativeDi").GetBoolean(),
                "Raw preflight Work evidence failed.");
        var protocols = evidence.GetProperty("deterministicProtocols");
        Require(protocols.GetProperty("schemaVersion").GetInt32() == 1 &&
            protocols.GetProperty("protocol").GetString() == "zendiator-perf03-raw-protocol-v1" &&
            protocols.GetProperty("producerSubstitution").GetBoolean() &&
            !protocols.GetProperty("actualWorkPendingFractionClaim").GetBoolean() &&
            !protocols.GetProperty("isolatedSnapshotExecuted").GetBoolean() &&
            protocols.GetProperty("normalTypedNativeDi").GetBoolean() &&
            !protocols.GetProperty("apiCancellationContractClaim").GetBoolean(), "Deterministic protocol scope changed.");
        var expectedProtocols = new HashSet<(string Route, string Scenario)>();
        foreach (var route in new[] { "Stream0", "Relay5" })
            foreach (var scenario in new[] { "first-pending-success", "repeated-pending-success",
                "repeated-pending-failure", "pending-eof", "pending-cleanup-success", "pending-cleanup-failure",
                "before-registration", "inside-registration", "consumption-reentry",
                "synchronous-raw", "completed-fault", "completed-cancelled" })
                expectedProtocols.Add((route, scenario));
        var protocolRows = protocols.GetProperty("rows");
        Require(protocolRows.GetArrayLength() == expectedProtocols.Count, "Deterministic raw protocol coverage changed.");
        foreach (var row in protocolRows.EnumerateArray())
            Require(row.GetProperty("status").GetString() == "Passed" &&
                expectedProtocols.Remove((row.GetProperty("route").GetString()!, row.GetProperty("scenario").GetString()!)),
                "Deterministic protocol coverage is duplicate, substituted or failed.");
        return new(Convert.ToHexString(SHA256.HashData(bytes)), Perf03CountSites.MapSha256, Perf03CountSites.Sites.Length);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
