namespace Competitive;

// Fixed existing H definitions, not additional benchmark cases.
public static class HAllPeersTimingAdmission
{
    private static readonly string[] SuccessEvents =
        ["before:1", "before:2", "before:3", "handle", "after:3", "finally:3", "after:2", "finally:2", "after:1", "finally:1"];

    public static async Task<object> RunAsync()
    {
        var z = new HZendiatorBenchmarks();
        var m = new HMediatRHistoricalBenchmarks();
        var g = new HMediatorBenchmarks();
        var d = new HDispatchRBenchmarks();
        var i = new HImmediateBenchmarks();
        var rows = new List<object>();
        await Validate(z, z.Setup, z.WarmSend, z.FirstSend, rows);
        await Validate(m, m.Setup, () => new(m.WarmSend()), () => new(m.FirstSend()), rows);
        await Validate(g, g.Setup, g.WarmSend, g.FirstSend, rows);
        await Validate(d, d.Setup, d.WarmSend, d.FirstSend, rows);
        await Validate(i, i.Setup, i.WarmSend, i.FirstSend, rows);
        return new
        {
            identityBefore = IdentityBefore(),
            rows
        };
    }

    private static object IdentityBefore()
    {
        var work = new HWork();
        var request = new HRequest(7, false, CancellationToken.None);
        var identities = new List<bool>();
        for (int role = 1; role <= 3; role++)
        {
            var result = work.Before(role, work, request, CancellationToken.None);
            if (!result.IsCompletedSuccessfully)
                throw new InvalidOperationException("H identity Before must complete synchronously");
            bool same = ReferenceEquals(result.GetAwaiter().GetResult(), request);
            if (!same)
                throw new InvalidOperationException("Fixed H workload unexpectedly replaced the request");
            identities.Add(same);
        }
        return new
        {
            roles = new[] { 1, 2, 3 },
            identities,
            request.Phase,
            request.Accumulator
        };
    }

    private static async Task Validate(HPeerBenchState benchmark, Action setup,
        Func<ValueTask<int>> warm, Func<ValueTask<int>> first, List<object> rows)
    {
        setup();
        var work = benchmark.Workload;
        bool prepared = false;
        Exception? bodyFailure = null;
        try
        {
            work.Events.Clear();
            work.Handled.Clear();
            work.Record = true;
            int warmValue = await warm().ConfigureAwait(false);
            if (warmValue != 7 + HWork.Expected || !work.Events.SequenceEqual(SuccessEvents) || work.Handled.Count != 1)
                throw new InvalidOperationException("Actual compiled H WarmSend work/order differs");
            rows.Add(new
            {
                actualType = benchmark.GetType().FullName,
                method = "WarmSend",
                parameters = new Dictionary<string, object?>(),
                value = warmValue,
                calls = 1,
                events = work.Events.ToArray(),
                handled = work.Handled.Count
            });
            work.Record = false;
            benchmark.PrepareFresh();
            prepared = true;
            string[] firstEvents = [];
            int firstHandled = 0;
            for (int n = 0; n < HPeerBenchState.FreshCount; n++)
            {
                work.Record = n == 0;
                if (n == 0)
                {
                    work.Events.Clear();
                    work.Handled.Clear();
                }
                int result = await first().ConfigureAwait(false);
                if (result != 7 + HWork.Expected)
                    throw new InvalidOperationException("Actual compiled H FirstSend result differs");
                if (n == 0)
                {
                    if (!work.Events.SequenceEqual(SuccessEvents) || work.Handled.Count != 1)
                        throw new InvalidOperationException("Actual compiled H FirstSend work/order differs");
                    firstEvents = work.Events.ToArray();
                    firstHandled = work.Handled.Count;
                }
            }
            work.Record = false;
            benchmark.ReleaseFresh();
            prepared = false;
            rows.Add(new
            {
                actualType = benchmark.GetType().FullName,
                method = "FirstSend",
                parameters = new Dictionary<string, object?>(),
                value = 7 + HWork.Expected,
                calls = HPeerBenchState.FreshCount,
                events = firstEvents,
                handled = firstHandled,
                freshBatchReleased = true
            });
        }
        catch (Exception error)
        {
            bodyFailure = error;
            throw;
        }
        finally
        {
            work.Record = false;
            var errors = new List<Exception>();
            if (prepared)
            {
                try
                {
                    benchmark.ReleaseFresh();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            try
            {
                benchmark.Cleanup();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            if (errors.Count != 0)
            {
                if (bodyFailure is not null)
                    errors.Insert(0, bodyFailure);
                throw new AggregateException("H actual-method admission cleanup failed", errors);
            }
        }
    }
}
