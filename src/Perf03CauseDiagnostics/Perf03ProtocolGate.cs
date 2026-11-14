using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NativeEntry = Competitive.Generated.Perf03ProtocolStreams.IZendiator;

namespace Competitive.Perf03.Diagnostics;

internal static class Perf03ProtocolGate
{
    private static readonly string[] Scenarios =
    [
        "first-pending-success", "repeated-pending-success", "repeated-pending-failure", "pending-eof",
        "pending-cleanup-success", "pending-cleanup-failure", "before-registration", "inside-registration", "consumption-reentry",
        "synchronous-raw", "completed-fault", "completed-cancelled"
    ];

    internal static async Task<object> Run()
    {
        var rows = new List<object>();
        foreach (bool relay in new[] { false, true })
            foreach (string name in Scenarios)
                rows.Add(await Check(relay, name).ConfigureAwait(false));
        Require(rows.Count == 24, "Protocol preflight case set changed.");
        return new
        {
            schemaVersion = 1,
            protocol = "zendiator-perf03-raw-protocol-v1",
            producerSubstitution = true,
            actualWorkPendingFractionClaim = false,
            isolatedSnapshotExecuted = false,
            normalTypedNativeDi = true,
            apiCancellationContractClaim = false,
            scope = "Normal generated routes with a named gate producer; separate from Work and the isolated control.",
            attribution = "Immutable operation tickets in AsyncLocal scopes and captured callback entry/exit tickets.",
            rows
        };
    }

    private static async Task<object> Check(bool relay, string name)
    {
        using var scenario = new Perf03ProtocolScenario(name, relay);
        using var api = new CancellationTokenSource();
        using var provider = Perf03ProtocolRegistration.Create(scenario);
        using var scope = provider.CreateScope();
        var entry = scope.ServiceProvider.GetRequiredService<NativeEntry>();
        var token = relay && name != "completed-cancelled" ? default : api.Token;
        object request = relay ? new Relay5(3, true, scenario.Probe) : new Stream0(3, true, scenario.Probe);
        scenario.ExpectedRequest = request;
        scenario.ExpectedToken = token;
        scenario.ExpectedScope = scope.ServiceProvider;
        var sequence = relay ? entry.StreamAsync((Relay5)request, token) : entry.StreamAsync((Stream0)request, token);
        Require(scenario.Opens == 0 && scenario.Probe.Events.Count == 0, "Protocol sequence creation executed callbacks.");
        var cursor = sequence.GetAsyncEnumerator();
        Require(scenario.Opens == 0 && scenario.Pulls == 0, "Protocol enumerator acquisition opened the input.");
        Require(CurrentInvalid(cursor), "Protocol Current was valid before first Move.");
        var values = new List<int>();
        var pendingAtReturn = new List<bool>();
        string outcome = "Completed";
        bool terminal = false, reenteredDuringCompletion = false;
        string? oldExitTicket = null, newIssuedParentTicket = null;
        Exception? failure = null;
        try
        {
            if (name is not ("first-pending-success" or "synchronous-raw"))
            {
                Require(await cursor.MoveNextAsync().ConfigureAwait(false), "Protocol synchronous first item disappeared.");
                ReadItem();
            }

            if (name == "synchronous-raw")
            {
                foreach (var raw in scenario.MoveGates)
                {
                    var operation = Issue(raw);
                    pendingAtReturn.Add(!operation.IsCompleted);
                    Require(operation.IsCompleted && raw.Registrations == 0, "Precompleted success was treated as pending.");
                    bool moved = await operation.ConfigureAwait(false);
                    if (moved)
                        ReadItem();
                    else
                        terminal = true;
                }
                Require(terminal, "Precompleted EOF did not terminate.");
            }
            else if (name == "consumption-reentry")
            {
                var firstGate = scenario.MoveGates[0];
                var secondGate = scenario.MoveGates[1];
                var first = Issue(firstGate);
                Pending(first, firstGate);
                var awaiter = first.ConfigureAwait(false).GetAwaiter();
                ValueTask<bool> following = default;
                var reentered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                awaiter.UnsafeOnCompleted(() =>
                {
                    try
                    {
                        Require(awaiter.GetResult(), "Protocol consuming continuation lost its item.");
                        ReadItem();
                        reenteredDuringCompletion = firstGate.Completing && firstGate.CompletingThread == Environment.CurrentManagedThreadId;
                        Require(Perf03ProtocolAttribution.Current.Value?.Id == firstGate.Ticket.Id,
                            "Public consuming continuation lost the old operation ticket.");
                        following = Issue(secondGate);
                        Pending(following, secondGate);
                        reentered.SetResult(true);
                    }
                    catch (Exception error)
                    {
                        reentered.SetException(error);
                    }
                });
                firstGate.Complete();
                Require(reentered.Task.IsCompleted, "Protocol public reentry was not inline with the old raw callback.");
                await reentered.Task.ConfigureAwait(false);
                Require(reenteredDuringCompletion, "Protocol reentry occurred after the old raw completion returned.");
                Require(firstGate.Ticket.Id != secondGate.Ticket.Id && secondGate.IssuedParentTicket == firstGate.Ticket.Id,
                    "Protocol reentry did not issue a distinct operation from the old operation context.");
                oldExitTicket = firstGate.CallbackExitAmbientTicket;
                newIssuedParentTicket = secondGate.IssuedParentTicket;
                Require(firstGate.CallbackExitCapturedTicket == firstGate.Ticket.Id && oldExitTicket == firstGate.Ticket.Id,
                    "The old callback exit was attributed to the newer operation.");
                secondGate.Complete();
                Require(await following.ConfigureAwait(false), "Reentered protocol Move lost its item.");
                ReadItem();
            }
            else
            {
                var raw = scenario.CleanupGate ?? scenario.MoveGates[0];
                var operation = Issue(raw);
                bool race = name is "before-registration" or "inside-registration";
                bool precompleted = name is "completed-fault" or "completed-cancelled";
                if (!race && !precompleted)
                    Pending(operation, raw);
                else
                    pendingAtReturn.Add(!operation.IsCompleted);

                bool expectedFailure = name is "repeated-pending-failure" or "pending-cleanup-failure" or "completed-fault";
                Exception sentinel = scenario.ClassifierFailure ?? new ArithmeticException("Expected PERF-03 protocol sentinel.");
                if (precompleted)
                    Require(operation.IsCompleted && raw.Registrations == 0, "Completed failure was treated as pending.");
                if (!race && !precompleted)
                {
                    if (expectedFailure)
                        raw.Fail(sentinel);
                    else
                        raw.Complete();
                }
                try
                {
                    bool moved = await operation.ConfigureAwait(false);
                    Require(!expectedFailure, "Protocol failure did not propagate.");
                    Require(name != "completed-cancelled", "Completed cancellation did not propagate.");
                    bool expectedItem = name is not ("pending-eof" or "pending-cleanup-success");
                    Require(moved == expectedItem, "Protocol raw result changed.");
                    if (moved)
                        ReadItem();
                    else
                        terminal = true;
                }
                catch (ArithmeticException error)
                {
                    Require(expectedFailure && ReferenceEquals(error, sentinel), "Protocol failure changed its exception identity.");
                    outcome = "ArithmeticException";
                    terminal = true;
                }
                catch (OperationCanceledException error)
                {
                    Require(name == "completed-cancelled" && scenario.SourceCancellation is { } sourceCancellation &&
                        error.CancellationToken == sourceCancellation.Token && sourceCancellation.IsCancellationRequested &&
                        token.CanBeCanceled && !token.IsCancellationRequested && token != sourceCancellation.Token,
                        "Completed source cancellation was confused with API cancellation.");
                    outcome = "OperationCanceledException";
                    terminal = true;
                }
            }

            if (!terminal)
                Require(!await cursor.MoveNextAsync().ConfigureAwait(false), "Protocol EOF was not false.");
            Require(CurrentInvalid(cursor), "Protocol Current remained valid after termination.");
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            try
            {
                await cursor.DisposeAsync().ConfigureAwait(false);
            }
            catch when (failure is not null)
            {
            }
        }
        await cursor.DisposeAsync().ConfigureAwait(false);

        int[] expectedEvents = relay ? [1, 2, 3, 4, 5, 100] : [100];
        Require(scenario.Opens == 1 && scenario.Probe.Handlers == 1 && scenario.Disposals == 1 && scenario.Probe.Disposals == 1,
            "Protocol input startup or cleanup count changed.");
        Require(scenario.Probe.Events.SequenceEqual(expectedEvents), "Protocol Relay callback order changed.");
        Require(scenario.Reads == values.Count && scenario.Probe.Items == values.Count && values.SequenceEqual(Enumerable.Range(0, values.Count)),
            "Protocol Current was prefetched, duplicated, or lost.");
        int expectedItems = name switch
        {
            "first-pending-success" or "repeated-pending-failure" or "pending-eof" or "pending-cleanup-success" or "pending-cleanup-failure" or
                "synchronous-raw" or "completed-fault" or "completed-cancelled" => 1,
            "consumption-reentry" => 3,
            _ => 2
        };
        Require(values.Count == expectedItems, "Protocol consumed an unexpected number of items.");
        Require(scenario.Pulls == (name is "repeated-pending-failure" or "completed-fault" or "completed-cancelled" ? 2 : expectedItems + 1), "Protocol source was pulled ahead or after termination.");
        var cache = CachedObjects(entry);
        Require(scenario.Handler is not null && cache.Contains(scenario.Handler), "Protocol handler was not the cached native-DI receiver.");
        Require(cache.Contains(scope.ServiceProvider), "Protocol entry was not bound to the native scope.");
        foreach (var gate in AllGates(scenario))
        {
            if (gate.Registrations != 0)
                await gate.CallbackExited.ConfigureAwait(false);
            Require(gate.Consumptions == 1 && gate.StatusChecks > 0 && gate.ResultTicket == gate.Ticket.Id && gate.ResultAmbientTicket == gate.Ticket.Id,
                "Protocol raw result was not consumed exactly once in its own operation context.");
            bool race = gate.Kind == "before-registration";
            bool precompleted = gate.Kind.StartsWith("precompleted-", StringComparison.Ordinal);
            Require(precompleted ? gate.Registrations == 0 : race ? gate.Registrations is 0 or 1 : gate.Registrations == 1, "Protocol registration count changed.");
            Require(gate.ReturnedStatuses.Length == gate.StatusChecks, "Raw status return trace lost an observation.");
            if (precompleted)
            {
                string expectedStatus = gate.Kind == "precompleted-fault" ? "Faulted" : gate.Kind == "precompleted-cancelled" ? "Canceled" : "Succeeded";
                Require(gate.ReturnedStatuses.All(status => status == expectedStatus), "Completed raw source was classified as pending.");
            }
            Require(gate.Callbacks == gate.Registrations, "Protocol registration did not receive exactly one callback.");
            if (gate.Registrations != 0)
                Require(gate.RegisteredTicket == gate.Ticket.Id && gate.RegistrationParentTicket == gate.Ticket.Id &&
                    gate.CallbackEntryTicket == gate.Ticket.Id && gate.CallbackExitCapturedTicket == gate.Ticket.Id && gate.CallbackExitAmbientTicket == gate.Ticket.Id,
                    "Protocol callback lifecycle changed operation attribution.");
            if (gate.Kind is "before-registration" or "inside-registration" or "manual")
                Require(gate.InlineCallbacks == gate.Registrations, "Protocol registration callback did not execute inline.");
        }

        return new
        {
            route = relay ? "Relay5" : "Stream0",
            scenario = name,
            status = "Passed",
            expectedRepeatedMovePath = relay ? "GroupPending" : "CompletionCell",
            inputCursorType = typeof(Perf03ProtocolCursor).FullName,
            typedEntryType = entry.GetType().FullName,
            nativeTypedEntryObserved = true,
            handlerRequestReferenceEquals = true,
            handlerApiTokenEquals = true,
            handlerNativeScopeReferenceEquals = true,
            cachedHandlerReferenceEquals = true,
            enumerationTokenDefault = true,
            tokenMode = token.CanBeCanceled ? "Live" : "Default",
            apiTokenCanBeCanceled = token.CanBeCanceled,
            apiTokenCancellationRequested = token.IsCancellationRequested,
            sourceCancellationIndependent = name == "completed-cancelled",
            apiCancellationContractClaim = false,
            outcome,
            values,
            count = values.Count,
            sum = values.Sum(),
            pulls = scenario.Pulls,
            currentReads = scenario.Reads,
            disposals = scenario.Disposals,
            events = scenario.Probe.Events,
            pendingAtReturn,
            reenteredDuringCompletion,
            oldCallbackExitTicket = oldExitTicket,
            newOperationIssuedParentTicket = newIssuedParentTicket,
            rawMoves = scenario.MoveGates.Select(g => g.Snapshot()).ToArray(),
            rawCleanup = scenario.CleanupGate?.Snapshot()
        };

        ValueTask<bool> Issue(Perf03ProtocolRawGate gate)
        {
            using var attribution = gate.Issue();
            return cursor.MoveNextAsync();
        }

        void Pending(ValueTask<bool> operation, Perf03ProtocolRawGate gate)
        {
            pendingAtReturn.Add(!operation.IsCompleted);
            Require(!operation.IsCompleted && gate.Consumptions == 0 && gate.Registrations == 1,
                "The host did not observe an unconsumed, registered pending raw operation.");
            Require(CurrentInvalid(cursor), "Protocol Current remained valid during a pending operation.");
        }

        void ReadItem()
        {
            int item = cursor.Current;
            Require(item == values.Count, "Protocol item order changed.");
            values.Add(item);
        }
    }

    private static IEnumerable<Perf03ProtocolRawGate> AllGates(Perf03ProtocolScenario scenario)
    {
        foreach (var gate in scenario.MoveGates)
            yield return gate;
        if (scenario.CleanupGate is { } cleanup)
            yield return cleanup;
    }

    private static HashSet<object> CachedObjects(object entry)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<object>();
        pending.Push(entry);
        while (pending.TryPop(out var value))
        {
            if (!seen.Add(value))
                continue;
            Require(seen.Count <= 64, "Protocol cache identity traversal exceeded its bound.");
            var type = value.GetType();
            if (!(type.Namespace ?? "").StartsWith("Competitive.Generated.Perf03ProtocolStreams", StringComparison.Ordinal))
                continue;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (!field.FieldType.IsValueType && field.GetValue(value) is { } child)
                    pending.Push(child);
        }
        return seen;
    }

    private static bool CurrentInvalid(IAsyncEnumerator<int> cursor)
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

    private static void Require(bool condition, string message) => Perf03ProtocolScenario.Check(condition, message);
}
