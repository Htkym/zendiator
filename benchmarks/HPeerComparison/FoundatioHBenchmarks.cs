using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using F = Foundatio.Mediator;

namespace Competitive;

// Add to the existing HPeerComparison project; retain its HPeerBenchState and HWork.
[MemoryDiagnoser]
public class HFoundatioBenchmarks : HPeerBenchState
{
    private F.IMediator _entry = null!;
    public CancellationToken ActiveToken => TokenSource.Token;

    [GlobalSetup]
    public void Setup()
    {
        TokenSource = new CancellationTokenSource();
        Success = new(7, false, TokenSource.Token);
        Failure = new(7, true, TokenSource.Token);
        Provider = HFoundatioRegistrationCandidate.Create(Workload);
        WarmScope = Provider.CreateScope();
        _entry = WarmScope.ServiceProvider.GetRequiredService<F.IMediator>();
        CheckWarm(_entry.InvokeAsync<int>(Success, TokenSource.Token));
        Capture();
    }

    [Benchmark, InvocationCount(FreshCount)]
    public ValueTask<int> FirstSend() => Fresh[Used++].ServiceProvider
        .GetRequiredService<F.IMediator>().InvokeAsync<int>(Success, TokenSource.Token);

    [Benchmark]
    public ValueTask<int> WarmSend() => _entry.InvokeAsync<int>(Success, TokenSource.Token);

    [Benchmark, InvocationCount(FreshCount)]
    public async ValueTask<int> ColdOuterFailure()
    {
        var entry = Fresh[Used++].ServiceProvider.GetRequiredService<F.IMediator>();
        try
        {
            await entry.InvokeAsync<int>(Failure, TokenSource.Token).ConfigureAwait(false);
        }
        catch (HOuterFailure)
        {
            return 1;
        }
        throw new InvalidOperationException("Expected outer Hook failure");
    }

    // Admission only: call the same compiled Benchmark methods with controlled state.
    public static async Task<HFoundatioTimingValidation> ValidateTimingPathsAsync()
    {
        HFoundatioRegistrationCandidate.ValidateBeforeIdentity();
        var benchmark = new HFoundatioBenchmarks();
        benchmark.Setup();
        try
        {
            var work = benchmark.Workload;
            string[] successEvents = ["before:1", "before:2", "before:3", "handle",
                "after:3", "finally:3", "after:2", "finally:2", "after:1", "finally:1"];
            string[] failureEvents = ["before:1", "error:1", "finally:1"];

            work.Events.Clear();
            work.Record = true;
            int warmValue = await benchmark.WarmSend().ConfigureAwait(false);
            if (warmValue != 7 + HWork.Expected)
                throw new InvalidOperationException("Foundatio H WarmSend result");
            RequireEvents(work.Events, successEvents, nameof(WarmSend));
            work.Record = false;

            await benchmark.ValidateFreshBatchAsync(false, successEvents).ConfigureAwait(false);
            await benchmark.ValidateFreshBatchAsync(true, failureEvents).ConfigureAwait(false);

            work.Events.Clear();
            work.Handled.Clear();
            work.Record = true;
            benchmark.TokenSource.Cancel();
            var canceled = benchmark.WarmSend();
            bool tokenMatches;
            try
            {
                await canceled.ConfigureAwait(false);
                throw new InvalidOperationException("Foundatio H pre-canceled WarmSend succeeded");
            }
            catch (OperationCanceledException error)
            {
                tokenMatches = error.CancellationToken == benchmark.ActiveToken;
                if (!tokenMatches || !benchmark.ActiveToken.IsCancellationRequested || work.Handled.Count != 0)
                    throw new InvalidOperationException("Foundatio H pre-canceled WarmSend token/handler boundary");
            }
            if (!canceled.IsCanceled)
                throw new InvalidOperationException("Foundatio H cancellation was not classified Canceled");
            return new(FreshCount, FreshCount, successEvents, failureEvents,
                work.Events.ToArray(), tokenMatches, canceled.IsCanceled, benchmark.GetType().FullName!,
                new Dictionary<string, object?>(), warmValue, true, true);
        }
        finally
        {
            benchmark.Workload.Record = false;
            benchmark.Cleanup();
        }
    }

    private async Task ValidateFreshBatchAsync(bool failure, string[] firstEvents)
    {
        PrepareFresh();
        Exception? bodyFailure = null;
        try
        {
            for (int i = 0; i < FreshCount; i++)
            {
                Workload.Record = i == 0;
                if (i == 0)
                    Workload.Events.Clear();
                int result = failure
                    ? await ColdOuterFailure().ConfigureAwait(false)
                    : await FirstSend().ConfigureAwait(false);
                if (result != (failure ? 1 : 7 + HWork.Expected))
                    throw new InvalidOperationException("Foundatio H cold method result");
                if (i == 0)
                    RequireEvents(Workload.Events, firstEvents, failure ? nameof(ColdOuterFailure) : nameof(FirstSend));
            }
        }
        catch (Exception error)
        {
            bodyFailure = error;
            throw;
        }
        finally
        {
            Workload.Record = false;
            try
            {
                ReleaseFresh();
            }
            catch (Exception cleanupFailure) when (bodyFailure is not null)
            {
                // Preserve the first semantic error and the unchanged partial-batch guard.
                throw new AggregateException("Foundatio H admission and fresh-scope cleanup failed", bodyFailure, cleanupFailure);
            }
        }
    }

    private static void RequireEvents(IEnumerable<string> actual, string[] expected, string method)
    {
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException($"Foundatio H {method} callback order");
    }
}

public sealed record HFoundatioTimingValidation(int FirstSendCalls, int ColdOuterFailureCalls,
    string[] SuccessEvents, string[] FailureEvents, string[] PreCanceledWarmEvents,
    bool CancellationTokenMatches, bool CanceledStatus, string ActualType,
    Dictionary<string, object?> Parameters, int WarmValue, bool FirstBatchReleased, bool ColdBatchReleased);
