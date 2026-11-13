using Competitive;

namespace Competitive.FoundatioRelease;

// Source only: these are admission operations, never extra benchmark workloads.
// The delegates below enter the same public compiled methods selected by BDN.
public static class FoundatioSemanticValidation
{
    public static async Task<object> RunAsync()
    {
        var records = new List<object>();
        foreach (var (name, depth, create) in new (string, int, Func<FoundatioState>)[]
        {
            (nameof(FoundatioSend0), 0, () => new FoundatioSend0()),
            (nameof(FoundatioSend1), 1, () => new FoundatioSend1()),
            (nameof(FoundatioSend3), 3, () => new FoundatioSend3()),
            (nameof(FoundatioSend5), 5, () => new FoundatioSend5())
        })
        {
            await CheckSendAsync(name, depth, create, records);
        }
        foreach (var (name, depth, relay, create) in new (string, int, bool, Func<FoundatioState>)[]
        {
            (nameof(FoundatioStream0), 0, false, () => new FoundatioStream0()),
            (nameof(FoundatioStream1), 1, false, () => new FoundatioStream1()),
            (nameof(FoundatioStream3), 3, false, () => new FoundatioStream3()),
            (nameof(FoundatioStream5), 5, false, () => new FoundatioStream5()),
            (nameof(FoundatioRelay1), 1, true, () => new FoundatioRelay1()),
            (nameof(FoundatioRelay5), 5, true, () => new FoundatioRelay5()),
            (nameof(FoundatioSameProducerStream0), 0, false, () => new FoundatioSameProducerStream0())
        })
        {
            foreach (bool asynchronous in new[] { false, true })
            {
                await CheckStreamAsync(name, depth, relay, asynchronous, create, records);
            }
        }
        object features = await FeaturesAdmission.ValidateTimingPathsAsync();
        return new
        {
            schema = "foundatio-source-candidate-semantic-gate-v8",
            status = "passed-only-if-this-program-completes",
            actualCompiledBenchmarkMethods = true,
            inputControls = "Probe and Token fields; timing defaults null/None",
            records,
            features,
            notCovered = new[]
            {
                "Actual BDN wrapper / native interceptor location and IL admission remains a separate gate",
                "H callback priority uses the separate shared HWork gate overlay and H profile CLI, not this main profile",
                "Arbitrary request replacement cannot be forwarded by Foundatio HandlerExecutionDelegate next()",
                "No performance or suspension-rate conclusion follows from semantic execution"
            }
        };
    }

    private static async Task CheckSendAsync(string name, int depth, Func<FoundatioState> create, List<object> records)
    {
        var state = create();
        var probe = new Probe();
        state.Probe = probe;
        state.Setup();
        var target = (IFoundatioSendProbe)state;
        try
        {
            foreach (var (method, operations, expected, call) in new (string, int, int, Func<ValueTask<int>>)[]
            {
                ("Typed", 1, 42, target.Typed), ("ResolveSend", 1, 42, target.ResolveSend),
                ("ScopeK1", 1, 42, target.ScopeK1), ("ScopeK10", 10, 420, target.ScopeK10),
                ("ScopeK100", 100, 4200, target.ScopeK100)
            })
            {
                Clear(probe);
                Require(await call() == expected, $"{name}.{method}: result");
                RequireSend(probe, depth, operations, $"{name}.{method}");
                records.Add(FoundatioSemanticSnapshot.Capture(state, method, "normal", probe, expected, operations));
            }
            Clear(probe);
            Require(target.ScopeOnly() == 1 && target.ScopeResolve() == 1 && probe.Handlers == 0,
                $"{name}: scope-only/resolve must not run handler");
            records.Add(FoundatioSemanticSnapshot.Capture(state, "ScopeOnly", "normal", probe, 1));
            records.Add(FoundatioSemanticSnapshot.Capture(state, "ScopeResolve", "normal", probe, 1));
            target.CreateFresh();
            Clear(probe);
            Exception? firstBatchError = null;
            try
            {
                for (int i = 0; i < FoundatioFreshScopes.Count; i++)
                    Require(await target.FirstSend() == 42, $"{name}.FirstSend: result");
            }
            catch (Exception error)
            {
                firstBatchError = error;
                throw;
            }
            finally
            {
                try
                {
                    target.DisposeFresh();
                }
                catch (Exception cleanup) when (firstBatchError is not null)
                {
                    throw new AggregateException(firstBatchError, cleanup);
                }
            }
            RequireSend(probe, depth, FoundatioFreshScopes.Count, $"{name}.FirstSend");
            records.Add(new
            {
                type = name,
                method = "FirstSend",
                operations = FoundatioFreshScopes.Count,
                scopeBoundary = "pre-created; resolution and dispatch inside method; disposal outside"
            });
            records.Add(FoundatioSemanticSnapshot.Capture(state, "FirstSend", "normal", probe, 42, FoundatioFreshScopes.Count));

            Clear(probe);
            probe.Fail = true;
            bool caught = false;
            try
            {
                await target.Typed();
            }
            catch (ArithmeticException error) when (error.Message == "Expected correctness sentinel")
            {
                caught = true;
            }
            Require(caught && probe.Handlers == 1, $"{name}.Typed: handler failure");
            records.Add(FoundatioSemanticSnapshot.Capture(state, "Typed", "handler-fault", probe));
            probe.Fail = false;

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            state.Token = cancellation.Token;
            Clear(probe);
            var cancelled = await ObserveCancellation(target.Typed);
            Require(probe.Handlers == 0, $"{name}.Typed: cancelled handler ran");
            records.Add(new
            {
                type = name,
                method = "Typed",
                input = "pre-cancelled",
                observation = cancelled
            });
            records.Add(FoundatioSemanticSnapshot.Capture(state, "Typed", "pre-cancelled", probe, cancellation: cancelled));

            // Exercise the real FirstSend call site once with cancellation; finish the
            // remaining batch normally so its unchanged full-iteration guard remains valid.
            target.CreateFresh();
            Clear(probe);
            Exception? cancelledBatchError = null;
            try
            {
                var firstCancelled = await ObserveCancellation(target.FirstSend);
                Require(probe.Handlers == 0, $"{name}.FirstSend: cancelled handler ran");
                records.Add(FoundatioSemanticSnapshot.Capture(state, "FirstSend", "pre-cancelled", probe, cancellation: firstCancelled));
                state.Token = CancellationToken.None;
                for (int i = 1; i < FoundatioFreshScopes.Count; i++)
                    Require(await target.FirstSend() == 42, $"{name}.FirstSend: batch completion");
                records.Add(new
                {
                    type = name,
                    method = "FirstSend",
                    input = "first cancelled; remainder normal",
                    observation = firstCancelled,
                    operations = FoundatioFreshScopes.Count
                });
            }
            catch (Exception error)
            {
                cancelledBatchError = error;
                throw;
            }
            finally
            {
                try
                {
                    target.DisposeFresh();
                }
                catch (Exception cleanup) when (cancelledBatchError is not null)
                {
                    throw new AggregateException(cancelledBatchError, cleanup);
                }
            }
        }
        finally
        {
            state.Cleanup();
        }
    }

    private static async Task CheckStreamAsync(string name, int depth, bool relay, bool asynchronous,
        Func<FoundatioState> create, List<object> records)
    {
        foreach (string method in new[] { "Full", "First", "EarlyBreak", "Creation", "Cancellation", "FullFault", "FullCancelled" })
        {
            var state = create();
            var target = (IFoundatioStreamProbe)state;
            var probe = new Probe();
            state.Probe = probe;
            target.Count = 16;
            target.Asynchronous = asynchronous;
            using var cancellation = new CancellationTokenSource();
            if (method == "FullCancelled")
            {
                cancellation.Cancel();
                state.Token = cancellation.Token;
            }
            if (method == "FullFault")
                probe.Fail = true;
            state.Setup();
            try
            {
                object? cancellationObservation = null;
                if (method == "Full")
                {
                    Require(await target.Full() == 120, $"{name}.Full: sum");
                    Require(probe.Items == 16 && probe.Handlers == 1 && probe.Disposals == 1, $"{name}.Full: lifecycle");
                    RequireOrder(probe, depth, name);
                }
                else if (method is "First" or "EarlyBreak")
                {
                    if (method == "First")
                        Require(await target.First(), $"{name}.First: missing item");
                    else
                        Require(await target.EarlyBreak() == 0, $"{name}.EarlyBreak: value");
                    Require(probe.Items == 1 && probe.Handlers == 1 && probe.Disposals == 1, $"{name}.{method}: lifecycle");
                    RequireOrder(probe, depth, name);
                }
                else if (method == "Creation")
                {
                    var sequence = target.Creation();
                    int atCreation = probe.Events.Count;
                    await using (var enumerator = sequence.GetAsyncEnumerator())
                    {
                        int atEnumerator = probe.Events.Count;
                        Require(await enumerator.MoveNextAsync(), $"{name}.Creation: missing item");
                        RequireOrder(probe, depth, name);
                        if (!relay)
                            Require(atCreation == 0 && atEnumerator == 0, $"{name}: regular preprocessing started early");
                        records.Add(new
                        {
                            type = name,
                            method,
                            asynchronous,
                            atCreation,
                            atEnumerator,
                            atFirstMove = probe.Events.Count,
                            interpretation = relay ? "record native Relay start boundary" : "first-Move deferred"
                        });
                        records.Add(FoundatioSemanticSnapshot.Capture(state, "Creation", "creation-boundary", probe,
                            new
                            {
                                atCreation,
                                atEnumerator,
                                atFirstMove = probe.Events.Count
                            }));
                    }
                    Require(probe.Disposals == 1, $"{name}.Creation: dispose");
                    records.Add(FoundatioSemanticSnapshot.Capture(state, "Creation", "normal", probe));
                    Clear(probe);
                    int repeatedSum = 0;
                    await foreach (var item in sequence.WithCancellation(state.Token).ConfigureAwait(false))
                        repeatedSum += item;
                    Require(repeatedSum == 120 && probe.Handlers == 1 && probe.Items == 16 && probe.Disposals == 1,
                        $"{name}.Creation: same-sequence independent re-enumeration lifecycle");
                    Require(probe.Events.SequenceEqual(relay ? new[] { 100 } : Enumerable.Range(1, depth).Append(100)),
                        $"{name}.Creation: same-sequence re-enumeration preprocessing boundary");
                    records.Add(FoundatioSemanticSnapshot.Capture(state, "Creation", "same-sequence-reenumeration", probe, repeatedSum));
                }
                else if (method == "Cancellation")
                {
                    Require(await target.Cancellation(), $"{name}.Cancellation: not observed");
                    Require(probe.Items == 1 && probe.Disposals == 1, $"{name}.Cancellation: cleanup/consumption");
                }
                else if (method == "FullFault")
                {
                    bool caught = false;
                    try
                    {
                        await target.Full();
                    }
                    catch (ArithmeticException error) when (error.Message == "Expected correctness sentinel")
                    {
                        caught = true;
                    }
                    Require(caught && probe.Handlers == 1 && probe.Items == 0 && probe.Disposals == 1, $"{name}.Full: fault cleanup");
                }
                else
                {
                    object observation = await ObserveCancellation(target.Full);
                    cancellationObservation = observation;
                    Require(probe.Handlers == 0 && probe.Items == 0 && probe.Disposals == 0, $"{name}.Full: pre-cancelled producer started");
                    records.Add(new
                    {
                        type = name,
                        method = "Full",
                        input = "pre-cancelled",
                        asynchronous,
                        observation
                    });
                }
                records.Add(new
                {
                    type = name,
                    method,
                    asynchronous,
                    probe.Handlers,
                    probe.Items,
                    probe.Disposals
                });
                if (method != "Creation")
                {
                    string actualMethod = method is "FullFault" or "FullCancelled" ? "Full" : method;
                    string scenario = method == "FullFault" ? "handler-fault" : method == "FullCancelled" ? "pre-cancelled" : "normal";
                    object? value = method switch
                    {
                        "Full" => 120,
                        "First" => true,
                        "EarlyBreak" => 0,
                        "Cancellation" => true,
                        _ => null
                    };
                    records.Add(FoundatioSemanticSnapshot.Capture(state, actualMethod, scenario, probe, value, cancellation: cancellationObservation));
                }
            }
            finally
            {
                state.Cleanup();
            }
        }
        // Verify the same full benchmark method with the other inherited input size.
        var longState = create();
        var longTarget = (IFoundatioStreamProbe)longState;
        var longProbe = new Probe();
        longState.Probe = longProbe;
        longTarget.Count = 1024;
        longTarget.Asynchronous = asynchronous;
        longState.Setup();
        try
        {
            Require(await longTarget.Full() == 523776 && longProbe.Items == 1024 && longProbe.Disposals == 1,
                $"{name}.Full1024: work/lifecycle");
            RequireOrder(longProbe, depth, name);
            records.Add(new
            {
                type = name,
                method = "Full",
                count = 1024,
                asynchronous
            });
            records.Add(FoundatioSemanticSnapshot.Capture(longState, "Full", "normal", longProbe, 523776));
        }
        finally
        {
            longState.Cleanup();
        }
    }

    private static async Task<object> ObserveCancellation(Func<ValueTask<int>> operation)
    {
        Task<int>? task = null;
        try
        {
            task = operation().AsTask();
            await task;
        }
        catch (OperationCanceledException error)
        {
            return new
            {
                exception = error.GetType().FullName,
                phase = task is null ? "call" : "await",
                status = task?.Status.ToString(),
                tokenCanBeCancelled = error.CancellationToken.CanBeCanceled
            };
        }
        throw new InvalidOperationException("Expected cancellation from actual benchmark method");
    }
    private static void Clear(Probe probe)
    {
        probe.Events.Clear();
        probe.Handlers = 0;
        probe.Items = 0;
        probe.Disposals = 0;
    }
    private static void RequireSend(Probe probe, int depth, int operations, string name)
    {
        Require(probe.Handlers == operations && probe.Events.Count == operations * (depth + 1), $"{name}: work count");
        int[] one = Enumerable.Range(1, depth).Append(100).ToArray();
        Require(probe.Events.SequenceEqual(Enumerable.Range(0, operations).SelectMany(_ => one)), $"{name}: work order");
    }
    private static void RequireOrder(Probe probe, int depth, string name) =>
        Require(probe.Events.SequenceEqual(Enumerable.Range(1, depth).Append(100)), $"{name}: preprocessing order");
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
