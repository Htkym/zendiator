namespace Competitive;

// Admission only. Every delegate enters its real compiled ColdOuterFailure method.
public static class HAllPeersPriorityAdmission
{
    public static async Task<HPriorityValidation[]> RunAsync()
    {
        var z = new HZendiatorBenchmarks();
        var m = new HMediatRHistoricalBenchmarks();
        var g = new HMediatorBenchmarks();
        var d = new HDispatchRBenchmarks();
        var i = new HImmediateBenchmarks();
        var f = new HFoundatioBenchmarks();
        return
        [
            await ValidateAsync("Zendiator", z, z.Setup, z.ColdOuterFailure).ConfigureAwait(false),
            await ValidateAsync("MediatRHistorical", m, m.Setup, m.ColdOuterFailure).ConfigureAwait(false),
            await ValidateAsync("Mediator", g, g.Setup, g.ColdOuterFailure).ConfigureAwait(false),
            await ValidateAsync("DispatchR", d, d.Setup, d.ColdOuterFailure).ConfigureAwait(false),
            await ValidateAsync("Immediate", i, i.Setup, i.ColdOuterFailure).ConfigureAwait(false),
            await ValidateAsync("Foundatio", f, f.Setup, f.ColdOuterFailure).ConfigureAwait(false)
        ];
    }

    public static async Task<HPriorityValidation> ValidateAsync(string peer, HPeerBenchState benchmark,
        Action setup, Func<ValueTask<int>> coldOuterFailure)
    {
        setup();
        var work = benchmark.Workload;
        bool prepared = false;
        Exception? bodyFailure = null;
        try
        {
            benchmark.PrepareFresh();
            prepared = true;
            var observerReplacement = new ArgumentException("priority-observer-replacement");
            var finallyFailure = new InvalidOperationException("priority-finally-failure");
            work.Events.Clear();
            work.Handled.Clear();
            work.ObservedError = null;
            work.ObservedExit = null;
            work.Error1Replacement = observerReplacement;
            work.Finally1Failure = finallyFailure;
            work.Record = true;

            bool finallyWon = false;
            try
            {
                await coldOuterFailure().ConfigureAwait(false);
                throw new InvalidOperationException("Expected priority finally failure to escape the benchmark consumer");
            }
            catch (Exception error) when (ReferenceEquals(error, finallyFailure))
            {
                finallyWon = true;
            }

            string[] events = work.Events.ToArray();
            Exception? observedError = work.ObservedError;
            if (observedError is not HOuterFailure || work.ObservedExit is not { } exit || exit.Success ||
                !ReferenceEquals(exit.Error, observerReplacement) || !finallyWon || work.Handled.Count != 0 ||
                !events.SequenceEqual(new[] { "before:1", "error:1", "finally:1" }))
                throw new InvalidOperationException($"{peer}: observer/finally priority or entered-stage boundary changed");

            string observedOriginalType = observedError.GetType().FullName!;
            work.Record = false;
            work.Error1Replacement = null;
            work.Finally1Failure = null;
            for (int n = 1; n < HPeerBenchState.FreshCount; n++)
                if (await coldOuterFailure().ConfigureAwait(false) != 1)
                    throw new InvalidOperationException($"{peer}: remaining ordinary cold failure result");

            return new(peer, HPeerBenchState.FreshCount, observedOriginalType,
                true, true, finallyWon, events, benchmark.GetType().FullName!, "ColdOuterFailure",
                new Dictionary<string, object?>(), HPeerBenchState.FreshCount - 1, work.Handled.Count);
        }
        catch (Exception error)
        {
            bodyFailure = error;
            throw;
        }
        finally
        {
            work.Record = false;
            work.Error1Replacement = null;
            work.Finally1Failure = null;
            List<Exception> cleanupErrors = [];
            if (prepared)
            {
                try
                {
                    benchmark.ReleaseFresh();
                }
                catch (Exception error)
                {
                    cleanupErrors.Add(error);
                }
            }
            try
            {
                benchmark.Cleanup();
            }
            catch (Exception error)
            {
                cleanupErrors.Add(error);
            }
            if (cleanupErrors.Count != 0)
            {
                if (bodyFailure is not null)
                    cleanupErrors.Insert(0, bodyFailure);
                throw new AggregateException($"{peer}: priority admission/cleanup failure", cleanupErrors);
            }
        }
    }
}

public sealed record HPriorityValidation(string Peer, int ColdOuterFailureCalls,
    string ObservedOriginalExceptionType, bool ObserverSawOriginalOuterFailure,
    bool FinallySawObserverReplacement, bool FinallyFailureEscaped, string[] Events,
    string ActualType, string Method, Dictionary<string, object?> Parameters, int OrdinaryColdCalls, int Handled);
