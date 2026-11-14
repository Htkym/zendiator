using System.Reflection;
using System.Text.Json;
using Competitive.Perf03.Diagnostics;

namespace Competitive;

internal static class Perf03CauseGate
{
    private const int Count = 1024;
    private const int Sum = 523776;
    private static readonly AsyncLocal<string?> Ambient = new();

    internal static async Task Run(string outputDirectory, string implementationSha256)
    {
        Perf03CauseVariant.RequireBoundCountedEvidence();
        var normal = new List<Dictionary<string, object?>>();
        var controls = new List<Dictionary<string, object?>>();
        var identities = new List<CaseIdentity>();
        foreach (bool iterator in new[] { false, true })
            foreach (bool live in new[] { false, true })
                foreach (bool relay in new[] { false, true })
                    foreach (bool asynchronous in new[] { false, true })
                    {
                        using var session = new Perf03CauseSession(iterator, live, relay, Count, asynchronous);
                        var identity = Identity(session, "Full");
                        normal.Add((await Observe(session, identity).ConfigureAwait(false)).Row);
                        identities.Add(identity);
                        if (!relay)
                        {
                            identity = Identity(session, "FullDirect");
                            normal.Add((await Observe(session, identity).ConfigureAwait(false)).Row);
                            identities.Add(identity);
                        }
                    }

        foreach (bool relay in new[] { false, true })
        {
            using var session = new Perf03CauseSession(false, !relay, relay, Count, true);
            var identity = Identity(session, "FullDiagnostic");
            var sharedProbe = new Probe();
            object sharedRequest = session.Request(sharedProbe);
            var ordinary = await Observe(session, Identity(session, "Full"), sharedRequest, sharedProbe).ConfigureAwait(false);
            sharedProbe.Events.Clear();
            sharedProbe.Handlers = sharedProbe.Items = sharedProbe.Disposals = 0;
            var isolated = await Observe(session, identity, sharedRequest, sharedProbe).ConfigureAwait(false);
            Require(ReferenceEquals(ordinary.Entry, isolated.Entry) && ReferenceEquals(ordinary.Handler, isolated.Handler),
                "The isolated control changed the cached native entry or handler.");
            Require(ordinary.Dependencies.Length == isolated.Dependencies.Length &&
                ordinary.Dependencies.All(x => isolated.Dependencies.Any(y => ReferenceEquals(x, y))),
                "The isolated control changed a cached Relay dependency.");
            Require(ordinary.SourceType == isolated.SourceType, "The isolated control changed the producer cursor.");
            foreach (string key in new[] { "count", "sum", "handlers", "items", "disposals" })
                Require(Equals(ordinary.Row[key], isolated.Row[key]), "The isolated control changed " + key + ".");
            isolated.Row["normalParity"] = Perf03CauseVariant.Id == "V0";
            isolated.Row["sequentialEnvelopeParity"] = true;
            isolated.Row["sharedEntryAndCachedHandler"] = true;
            isolated.Row["sharedRequest"] = true;
            isolated.Row["contractChecks"] = await CheckContracts(session).ConfigureAwait(false);
            isolated.Row["selectedVariantProtocols"] = await CheckSelectedVariantProtocols(session).ConfigureAwait(false);
            controls.Add(isolated.Row);
            identities.Add(identity);
        }

        Require(normal.Count == 24 && controls.Count == 2 && identities.Distinct().Count() == 26,
            "The PERF-03 gate case set changed.");
        string output = Path.Combine(outputDirectory, "perf03-cause");
        Directory.CreateDirectory(output);
        var evidence = new
        {
            schemaVersion = 1,
            protocol = "zendiator-perf03-cause-gate-v1",
            variant = Perf03CauseVariant.Id,
            variantMetadata = Perf03CauseVariant.Metadata,
            implementationSha256,
            caseSet = identities.OrderBy(x => x.Asynchronous ? "true" : "false", StringComparer.Ordinal)
                .ThenBy(x => x.Method, StringComparer.Ordinal).ThenBy(x => x.Type, StringComparer.Ordinal),
            normalInputCases = normal,
            isolatedControls = controls,
            pendingFractionClaim = false,
            variantEnvelope = new
            {
                sequential = true,
                oneEnumeratorPerOperation = true,
                awaitAndConsumeBeforeNextMove = true,
                savedUnconsumedValueTask = false,
                concurrentMoveOrDispose = false,
                failureAndCleanupChecked = true,
                productionGuaranteesClaimed = false,
                ambientContext = "Consumer AsyncLocal preservation only; producer context unprobed",
                unsupportedProbes = new[] { "saved-unconsumed-value-task", "concurrent-move-dispose", "raw-pending-decision-site-counts" }
            }
        };
        await File.WriteAllTextAsync(Path.Combine(output, "perf03-cause.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        Console.WriteLine("PERF03_CAUSE_PARITY_OK: 24 normal/direct inputs and 2 selected diagnostic routes; raw pending fraction unclaimed");
    }

    private static async Task<Observation> Observe(Perf03CauseSession session, CaseIdentity identity,
        object? sharedRequest = null, Probe? sharedProbe = null)
    {
        bool direct = identity.Method == "FullDirect";
        bool diagnostic = identity.Method == "FullDiagnostic";
        var probe = sharedProbe ?? new Probe();
        object request = sharedRequest ?? session.Request(probe);
        IAsyncEnumerable<int>? sequence = direct ? null : diagnostic
            ? session.OpenDiagnostic(request, session.ApiToken)
            : session.Open(request, session.ApiToken);
        var sequenceGraph = sequence is null ? new HashSet<object>(ReferenceEqualityComparer.Instance) : Graph(sequence);
        Require(probe.Handlers == 0 && probe.Events.Count == 0, "Stream creation ran producer callbacks.");
        var cursor = direct ? session.OpenDirect(probe) : sequence!.GetAsyncEnumerator();
        Require(probe.Handlers == 0 && probe.Events.Count == 0, "Enumerator acquisition ran producer callbacks.");
        int count = 0, sum = 0, moves = 0, consumed = 0, incomplete = 0, repeatedIncomplete = 0;
        bool firstIncomplete = false, eofIncomplete = false;
        object? handler = null;
        object[] dependencies = [];
        string? sourceType = null;
        bool requestObserved = false, sourceProbeObserved = false, tokenObserved = false;
        bool entryObserved = false, scopeObserved = false;
        try
        {
            while (true)
            {
                var operation = cursor.MoveNextAsync();
                bool pending = !operation.IsCompleted;
                moves++;
                if (pending)
                    incomplete++;
                bool moved = await operation.ConfigureAwait(false);
                consumed++;
                if (!moved)
                {
                    eofIncomplete = pending;
                    break;
                }
                if (count == 0)
                {
                    firstIncomplete = pending;
                    var graph = Graph(cursor);
                    var sources = graph.Where(IsInputCursor).ToArray();
                    Require(sources.Length == 1, "Expected one actual Work input cursor.");
                    object source = sources[0];
                    sourceType = source.GetType().FullName!;
                    sourceProbeObserved = Fields(source.GetType()).Any(f =>
                        f.FieldType == typeof(Probe) && ReferenceEquals(f.GetValue(source), probe));
                    var tokens = Fields(source.GetType()).Where(f => f.FieldType == typeof(CancellationToken))
                        .Select(f => (CancellationToken)f.GetValue(source)!).ToArray();
                    tokenObserved = tokens.Length != 0 && tokens.Any(t => t == session.ApiToken) &&
                        tokens.All(t => !t.CanBeCanceled || t == session.ApiToken);
                    Require(sourceProbeObserved && tokenObserved, "The actual input lost its probe or API token.");
                    if (!direct)
                    {
                        requestObserved = graph.Contains(request) || sequenceGraph.Contains(request);
                        entryObserved = graph.Contains(session.Entry) || sequenceGraph.Contains(session.Entry);
                        var entryGraph = Graph(session.Entry);
                        scopeObserved = entryGraph.Contains(session.ScopeIdentity);
                        var handlers = entryGraph.Where(x => x.GetType() == session.HandlerType).ToArray();
                        Require(handlers.Length == 1, "Expected exactly one cached native-DI handler receiver.");
                        handler = handlers[0];
                        dependencies = entryGraph.Where(x => x.GetType().Namespace == "Competitive" &&
                            x.GetType().Name.StartsWith("ZRBehavior", StringComparison.Ordinal)).ToArray();
                        Require(dependencies.Length == (session.Relay ? 5 : 0), "Cached Relay dependency set changed.");
                        Require(requestObserved && entryObserved && scopeObserved,
                            "The active route lost request, native entry, or native scope identity.");
                    }
                }
                else if (pending)
                    repeatedIncomplete++;
                Require(cursor.Current == count, "The PERF-03 input sequence changed.");
                sum += cursor.Current;
                count++;
            }
        }
        finally
        {
            await cursor.DisposeAsync().ConfigureAwait(false);
        }
        int[] expectedEvents = session.Relay && !direct ? [1, 2, 3, 4, 5, 100] : [100];
        Require(count == Count && sum == Sum && moves == Count + 1 && consumed == moves,
            "The PERF-03 full-consumption result changed.");
        Require(probe.Handlers == 1 && probe.Items == Count && probe.Disposals == 1 && probe.Events.SequenceEqual(expectedEvents),
            "The PERF-03 producer callbacks or cleanup changed.");
        var row = new Dictionary<string, object?>
        {
            ["case"] = identity,
            ["status"] = "Passed",
            ["count"] = count,
            ["sum"] = sum,
            ["handlers"] = probe.Handlers,
            ["items"] = probe.Items,
            ["disposals"] = probe.Disposals,
            ["events"] = probe.Events.ToArray(),
            ["producerRepresentation"] = session.Iterator ? "Iterator" : "Named",
            ["relayDepth"] = session.Relay ? 5 : 0,
            ["tokenMode"] = session.Live ? "Live" : "Default",
            ["enumerationTokenDefault"] = true,
            ["publicEnumerationTokenApplicable"] = !direct,
            ["sourceTokenMatchesApi"] = tokenObserved,
            ["nativeDiHandlerObserved"] = handler is not null,
            ["identities"] = new
            {
                requestIdentityApplicable = !direct,
                requestReferenceObserved = requestObserved,
                sourceProbeReferenceObserved = sourceProbeObserved,
                nativeEntryReferenceObserved = entryObserved,
                nativeScopeReferenceObserved = scopeObserved,
                cachedHandlerType = handler?.GetType().FullName,
                cachedHandlerCount = handler is null ? 0 : 1,
                entryType = direct ? null : session.Entry.GetType().FullName,
                sourceCursorType = sourceType,
                cachedDependencyTypes = dependencies.Select(x => x.GetType().FullName).Order(StringComparer.Ordinal).ToArray()
            },
            ["consumerPreflight"] = new
            {
                moveCalls = moves,
                consumptions = consumed,
                incompleteAtReturn = incomplete,
                firstMoveIncomplete = firstIncomplete,
                repeatedItemIncomplete = repeatedIncomplete,
                eofIncomplete,
                rawDecisionSitesObserved = false,
                timingPendingFractionKnown = false
            }
        };
        return new(row, direct ? null : session.Entry, handler, dependencies, sourceType!);
    }

    private static async Task<object[]> CheckContracts(Perf03CauseSession session)
    {
        var rows = new List<object>();
        foreach (string name in new[] { "cold-disposal", "early-disposal", "producer-failure", "pre-cancelled-api", "cancel-after-first", "distinct-enumeration-token", "current-lifecycle", "ambient-context" }
            .Concat(session.Relay ? new[] { "relay-failure" } : []))
        {
            var ordinary = await Contract(session, name, false).ConfigureAwait(false);
            var isolated = await Contract(session, name, true).ConfigureAwait(false);
            Require(ordinary == isolated, "The isolated control contract differs: " + name + ".");
            rows.Add(new
            {
                name,
                status = "Passed",
                normal = JsonSerializer.Deserialize<JsonElement>(ordinary),
                isolated = JsonSerializer.Deserialize<JsonElement>(isolated)
            });
        }
        return rows.ToArray();
    }

    private static async Task<string> Contract(Perf03CauseSession session, string name, bool diagnostic)
    {
        var probe = new Probe { Fail = name == "producer-failure", FailRelay = name == "relay-failure" };
        using var api = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var token = name is "pre-cancelled-api" or "cancel-after-first" or "distinct-enumeration-token" ? api.Token : session.ApiToken;
        if (name == "pre-cancelled-api")
            api.Cancel();
        var request = session.Request(probe);
        var sequence = diagnostic ? session.OpenDiagnostic(request, token) : session.Open(request, token);
        var cursor = sequence.GetAsyncEnumerator(name == "distinct-enumeration-token" ? enumeration.Token : default);
        int items = 0;
        string outcome = "Completed";
        string? ambientBefore = Ambient.Value;
        Ambient.Value = "perf03-cause-gate";
        try
        {
            if (name == "current-lifecycle")
                Require(CurrentThrows(cursor), "Current was valid before first Move.");
            if (name != "cold-disposal")
                while (await cursor.MoveNextAsync().ConfigureAwait(false))
                {
                    Require(cursor.Current == items, "Contract probe sequence changed.");
                    items++;
                    if (name is "early-disposal" or "ambient-context")
                        break;
                    if (name == "cancel-after-first")
                        api.Cancel();
                    if (name == "distinct-enumeration-token")
                        enumeration.Cancel();
                }
            if (name == "current-lifecycle")
                Require(CurrentThrows(cursor), "Current remained valid after EOF.");
            if (name == "ambient-context")
                Require(Ambient.Value == "perf03-cause-gate", "V0 changed the consumer AsyncLocal value.");
        }
        catch (OperationCanceledException error)
        {
            Require(name is "pre-cancelled-api" or "cancel-after-first" or "distinct-enumeration-token",
                "Unexpected cancellation in PERF-03 contract probe.");
            Require(error.CancellationToken.IsCancellationRequested, "Cancellation exception lost a cancelled token.");
            outcome = "OperationCanceledException";
        }
        catch (ArithmeticException)
        {
            Require(name is "producer-failure" or "relay-failure", "Unexpected producer failure.");
            outcome = "ArithmeticException";
        }
        finally
        {
            try
            {
                await cursor.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                Ambient.Value = ambientBefore;
            }
        }
        string expectedOutcome = name switch
        {
            "producer-failure" or "relay-failure" => "ArithmeticException",
            "pre-cancelled-api" or "cancel-after-first" or "distinct-enumeration-token" => "OperationCanceledException",
            _ => "Completed"
        };
        Require(outcome == expectedOutcome, "Expected contract outcome was not observed: " + name + ".");
        bool started = name is not ("cold-disposal" or "pre-cancelled-api" or "relay-failure");
        Require(probe.Handlers == (started ? 1 : 0) && probe.Disposals == (started ? 1 : 0),
            "Contract probe changed producer startup or cleanup: " + name + ".");
        int expectedItems = name switch
        {
            "early-disposal" or "ambient-context" or "cancel-after-first" or "distinct-enumeration-token" => 1,
            "current-lifecycle" => Count,
            _ => 0
        };
        Require(items == expectedItems && probe.Items == items, "Contract probe changed item count: " + name + ".");
        return JsonSerializer.Serialize(new
        {
            outcome,
            items,
            probe.Handlers,
            probe.Disposals,
            events = probe.Events.ToArray()
        });
    }

    private static async Task<object> CheckSelectedVariantProtocols(Perf03CauseSession session)
    {
        bool rootOwned = Perf03CauseVariant.Id is "VRoot" or "VWord";
        await CheckInlineReentry();
        await CheckInlineDisposal();
        if (rootOwned)
        {
            await CheckInvalidatedValueTask();
            await CheckDurableTask();
        }
        return new
        {
            status = "Passed",
            uninstrumentedSelectedCandidate = true,
            producer = "WorkStreamCursor",
            producerSubstitution = false,
            controlledScheduling = true,
            pendingDisposeRejected = true,
            inlineConsumptionAndNextPendingMove = true,
            disposalInsideContinuation = true,
            oldUnconsumedValueTaskInvalidationChecked = rootOwned,
            durablePrecreatedTaskChecked = rootOwned,
            timedPendingFractionClaim = false
        };

        IAsyncEnumerator<int> Open() => session.OpenDiagnostic().GetAsyncEnumerator();

        static ValueTask<bool> Issue(IAsyncEnumerator<int> cursor, Competitive.Perf03.Counted.Perf03YieldPump pump)
        {
            ValueTask<bool> operation;
            using (pump.Enter())
                operation = cursor.MoveNextAsync();
            Require(!operation.IsCompleted && pump.PendingCount == 1,
                "The selected candidate did not hold one actual Work yield.");
            return operation;
        }

        static async Task First(IAsyncEnumerator<int> cursor, Competitive.Perf03.Counted.Perf03YieldPump pump)
        {
            var first = Issue(cursor, pump);
            pump.RunOne();
            Require(await first.ConfigureAwait(false) && cursor.Current == 0,
                "Selected candidate first Move changed the actual Work result.");
        }

        static async Task Close(IAsyncEnumerator<int> cursor, Competitive.Perf03.Counted.Perf03YieldPump pump)
        {
            while (pump.PendingCount != 0)
                pump.RunOne();
            await cursor.DisposeAsync().ConfigureAwait(false);
        }

        async Task CheckInlineReentry()
        {
            var cursor = Open();
            var pump = new Competitive.Perf03.Counted.Perf03YieldPump();
            try
            {
                await First(cursor, pump).ConfigureAwait(false);
                var pending = Issue(cursor, pump);
                Exception? disposeError = null;
                ValueTask? disposeAttempt = null;
                try
                {
                    disposeAttempt = cursor.DisposeAsync();
                }
                catch (Exception error) { disposeError = error; }
                if (disposeAttempt is { } attempt)
                {
                    Require(attempt.IsCompleted, "Pending disposal did not reject immediately.");
                    try
                    {
                        attempt.GetAwaiter().GetResult();
                    }
                    catch (Exception error) { disposeError = error; }
                }
                Require(disposeError is InvalidOperationException,
                    "Selected candidate did not reject disposal during a pending Move.");
                var awaiter = pending.ConfigureAwait(false).GetAwaiter();
                ValueTask<bool> next = default;
                bool entered = false;
                Exception? callbackError = null;
                awaiter.UnsafeOnCompleted(() =>
                {
                    try
                    {
                        entered = true;
                        Require(awaiter.GetResult() && cursor.Current == 1, "Inline consumption lost the old item.");
                        next = Issue(cursor, pump);
                    }
                    catch (Exception error) { callbackError = error; }
                });
                pump.RunOne();
                if (callbackError is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackError).Throw();
                Require(entered && !next.IsCompleted && pump.PendingCount == 1,
                    "Selected candidate did not reenter and hold the next pending Move inline.");
                pump.RunOne();
                Require(await next.ConfigureAwait(false) && cursor.Current == 2,
                    "Old publication damaged the next Move generation.");
            }
            finally { await Close(cursor, pump).ConfigureAwait(false); }
        }

        async Task CheckInlineDisposal()
        {
            var cursor = Open();
            var pump = new Competitive.Perf03.Counted.Perf03YieldPump();
            try
            {
                await First(cursor, pump).ConfigureAwait(false);
                var pending = Issue(cursor, pump);
                var awaiter = pending.ConfigureAwait(false).GetAwaiter();
                ValueTask disposal = default;
                bool entered = false;
                Exception? callbackError = null;
                awaiter.UnsafeOnCompleted(() =>
                {
                    try
                    {
                        entered = true;
                        Require(awaiter.GetResult() && cursor.Current == 1, "Inline disposal lost the old result.");
                        disposal = cursor.DisposeAsync();
                    }
                    catch (Exception error) { callbackError = error; }
                });
                pump.RunOne();
                if (callbackError is not null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackError).Throw();
                Require(entered, "Selected candidate disposal continuation was not inline.");
                await disposal.ConfigureAwait(false);
            }
            finally { await Close(cursor, pump).ConfigureAwait(false); }
        }

        async Task CheckInvalidatedValueTask()
        {
            var cursor = Open();
            var pump = new Competitive.Perf03.Counted.Perf03YieldPump();
            try
            {
                await First(cursor, pump).ConfigureAwait(false);
                var old = Issue(cursor, pump);
                pump.RunOne();
                Require(old.IsCompletedSuccessfully, "The saved old root operation did not complete.");
                var next = Issue(cursor, pump);
                bool rejected = false;
                try
                {
                    old.GetAwaiter().GetResult();
                }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Selected single issuer did not invalidate the old unconsumed ValueTask.");
                pump.RunOne();
                Require(await next.ConfigureAwait(false) && cursor.Current == 2,
                    "Rejected stale ValueTask damaged the next generation.");
            }
            finally { await Close(cursor, pump).ConfigureAwait(false); }
        }

        async Task CheckDurableTask()
        {
            var cursor = Open();
            var pump = new Competitive.Perf03.Counted.Perf03YieldPump();
            try
            {
                await First(cursor, pump).ConfigureAwait(false);
                var pending = Issue(cursor, pump);
                var savedTask = pending.AsTask();
                pump.RunOne();
                Require(savedTask.IsCompletedSuccessfully, "The precreated Task did not capture the old completion.");
                var next = Issue(cursor, pump);
                pump.RunOne();
                Require(await next.ConfigureAwait(false) && cursor.Current == 2,
                    "Selected root did not complete after the saved Task.");
                Require(await savedTask.ConfigureAwait(false) && await savedTask.ConfigureAwait(false),
                    "The already-created Task lost its durable result after source reuse.");
            }
            finally { await Close(cursor, pump).ConfigureAwait(false); }
        }
    }


    private static bool CurrentThrows(IAsyncEnumerator<int> cursor)
    {
        try
        {
            _ = cursor.Current;
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static CaseIdentity Identity(Perf03CauseSession session, string method) => new(
        "ZendiatorPerf03" + (session.Iterator ? "Iterator" : "Named") + (session.Live ? "Live" : "Default") + (session.Relay ? "Relay5" : "Stream0"),
        method, Count, session.Asynchronous);

    private static bool IsInputCursor(object value) => value is WorkStreamCursor ||
        value.GetType().DeclaringType == typeof(Work) && value.GetType().Name.StartsWith("<Stream>", StringComparison.Ordinal);

    private static HashSet<object> Graph(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current))
                continue;
            Require(seen.Count <= 256, "Diagnostic cursor identity traversal exceeded its fixed bound.");
            var type = current.GetType();
            string ns = type.Namespace ?? "";
            if (!ns.StartsWith("Competitive.Generated.", StringComparison.Ordinal) &&
                ns != "Competitive.Perf03.Diagnostics" && !IsInputCursor(current))
                continue;
            foreach (var field in Fields(type))
                if ((!field.FieldType.IsValueType ||
                    (field.FieldType.Namespace ?? "").StartsWith("Competitive.Generated.", StringComparison.Ordinal) ||
                    field.FieldType.Namespace == "Competitive.Perf03.Diagnostics") && field.GetValue(current) is { } child)
                    pending.Push(child);
        }
        return seen;
    }

    private static IEnumerable<FieldInfo> Fields(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return field;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record CaseIdentity(string Type, string Method, int Count, bool Asynchronous);
    private sealed record Observation(Dictionary<string, object?> Row, object? Entry, object? Handler, object[] Dependencies, string SourceType);
}
