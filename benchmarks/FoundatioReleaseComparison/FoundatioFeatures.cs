using BenchmarkDotNet.Attributes;
using Competitive;
using F = Foundatio.Mediator;

namespace Competitive.FoundatioRelease;

// Source candidates only. Admission needs generated/native DI and actual-child proof.

public sealed record FoundatioVoidRequest0(Counter Counter, Probe? Probe = null) : IWork;
[F.Handler]
public sealed class FoundatioVoid0Handler
{
    public ValueTask HandleAsync(FoundatioVoidRequest0 request, CancellationToken token)
    {
        Work.Void(request.Counter, request.Probe, token);
        return default;
    }
}

[MemoryDiagnoser]
public class FoundatioVoid0 : FoundatioState
{
    private readonly Counter counter = new();
    private FoundatioVoidRequest0 request = null!;
    public int CounterValue => counter.Value;
    protected override void InitializeRequest() => request = new(counter, Probe);

    public async ValueTask Invoke(FoundatioVoidRequest0 value, CancellationToken token = default) =>
        await Entry.InvokeAsync(value, token);

    [Benchmark]
    public async ValueTask Dispatch() => await Entry.InvokeAsync(request, Token);
}

public sealed record FoundatioVoidRequest1(Counter Counter, Probe? Probe = null) : ILevel1;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioBefore1Middleware))]
public sealed class FoundatioVoid1Handler
{
    public ValueTask HandleAsync(FoundatioVoidRequest1 request, CancellationToken token)
    {
        Work.Void(request.Counter, request.Probe, token);
        return default;
    }
}

[MemoryDiagnoser]
public class FoundatioVoid1 : FoundatioState
{
    private readonly Counter counter = new();
    private FoundatioVoidRequest1 request = null!;
    public int CounterValue => counter.Value;
    protected override void InitializeRequest() => request = new(counter, Probe);

    public async ValueTask Invoke(FoundatioVoidRequest1 value, CancellationToken token = default) =>
        await Entry.InvokeAsync(value, token);

    [Benchmark]
    public async ValueTask Dispatch() => await Entry.InvokeAsync(request, Token);
}

public sealed record FoundatioVoidRequest3(Counter Counter, Probe? Probe = null) : ILevel3;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioBefore1Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore2Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore3Middleware))]
public sealed class FoundatioVoid3Handler
{
    public ValueTask HandleAsync(FoundatioVoidRequest3 request, CancellationToken token)
    {
        Work.Void(request.Counter, request.Probe, token);
        return default;
    }
}

[MemoryDiagnoser]
public class FoundatioVoid3 : FoundatioState
{
    private readonly Counter counter = new();
    private FoundatioVoidRequest3 request = null!;
    public int CounterValue => counter.Value;
    protected override void InitializeRequest() => request = new(counter, Probe);

    public async ValueTask Invoke(FoundatioVoidRequest3 value, CancellationToken token = default) =>
        await Entry.InvokeAsync(value, token);

    [Benchmark]
    public async ValueTask Dispatch() => await Entry.InvokeAsync(request, Token);
}

public sealed record FoundatioVoidRequest5(Counter Counter, Probe? Probe = null) : ILevel5;
[F.Handler]
[F.UseMiddleware(typeof(FoundatioBefore1Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore2Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore3Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore4Middleware))]
[F.UseMiddleware(typeof(FoundatioBefore5Middleware))]
public sealed class FoundatioVoid5Handler
{
    public ValueTask HandleAsync(FoundatioVoidRequest5 request, CancellationToken token)
    {
        Work.Void(request.Counter, request.Probe, token);
        return default;
    }
}

[MemoryDiagnoser]
public class FoundatioVoid5 : FoundatioState
{
    private readonly Counter counter = new();
    private FoundatioVoidRequest5 request = null!;
    public int CounterValue => counter.Value;
    protected override void InitializeRequest() => request = new(counter, Probe);

    public async ValueTask Invoke(FoundatioVoidRequest5 value, CancellationToken token = default) =>
        await Entry.InvokeAsync(value, token);

    [Benchmark]
    public async ValueTask Dispatch() => await Entry.InvokeAsync(request, Token);
}

public sealed record FoundatioClosedGeneric<T>(int Value = 41, Probe? Probe = null) : IWork;
public sealed record FoundatioOpenGeneric<T>(int Value = 41, Probe? Probe = null) : IWork;

[F.Handler]
public sealed class FoundatioClosedHandler
{
    public ValueTask<int> HandleAsync(FoundatioClosedGeneric<int> request, CancellationToken token) =>
        new(Work.Handle(request.Value, request.Probe, token));
}

// Preserve an actual open handler. Do not replace it with a handwritten int handler.
[F.Handler]
public sealed class FoundatioOpenHandler<T>
{
    public ValueTask<int> HandleAsync(FoundatioOpenGeneric<T> request, CancellationToken token) =>
        new(Work.Handle(request.Value, request.Probe, token));
}

[MemoryDiagnoser]
public class FoundatioClosed : FoundatioState
{
    private FoundatioClosedGeneric<int> request = null!;
    protected override void InitializeRequest() => request = new(41, Probe);

    public async ValueTask<int> Invoke(FoundatioClosedGeneric<int> value, CancellationToken token = default) =>
        await Entry.InvokeAsync<int>(value, token);

    [Benchmark]
    public async ValueTask<int> Dispatch() => await Entry.InvokeAsync<int>(request, Token);
}

[MemoryDiagnoser]
public class FoundatioOpen : FoundatioState
{
    private FoundatioOpenGeneric<int> request = null!;
    protected override void InitializeRequest() => request = new(41, Probe);

    public async ValueTask<int> Invoke(FoundatioOpenGeneric<int> value, CancellationToken token = default) =>
        await Entry.InvokeAsync<int>(value, token);

    [Benchmark]
    public async ValueTask<int> Dispatch() => await Entry.InvokeAsync<int>(request, Token);
}

public sealed record FoundatioNotice0(Counter Counter, Probe? Probe = null, bool Asynchronous = false);

[MemoryDiagnoser]
public class FoundatioNotification0 : FoundatioState
{
    private readonly Counter counter = new();
    private FoundatioNotice0 request = null!;
    public int CounterValue => counter.Value;
    public int CounterActive => counter.Active;
    protected override void InitializeRequest() => request = new(counter, Probe);

    public async ValueTask Invoke(FoundatioNotice0 value, CancellationToken token = default) =>
        await Entry.PublishAsync(value, token);

    [Benchmark]
    public async ValueTask Dispatch() => await Entry.PublishAsync(request, Token);
}

public sealed record FoundatioNotice1(Counter Counter, Probe? Probe = null, bool Asynchronous = false);
[F.Handler(1)]
public sealed class FoundatioNotice1Handler1
{
    public ValueTask HandleAsync(FoundatioNotice1 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 1, token);
}

[MemoryDiagnoser]
public class FoundatioNotification1 : FoundatioState
{
    private readonly Counter counter = new();
    private FoundatioNotice1 request = null!;
    public int CounterValue => counter.Value;
    public int CounterActive => counter.Active;
    private FoundatioNotice1 asynchronousRequest = null!;
    public int AsynchronousCounterValue => asynchronousRequest.Counter.Value;
    public int AsynchronousCounterActive => asynchronousRequest.Counter.Active;
    protected override void InitializeRequest()
    {
        request = new(counter, Probe);
        asynchronousRequest = new(new Counter(), Probe, Asynchronous: true);
    }

    public async ValueTask Invoke(FoundatioNotice1 value, CancellationToken token = default) =>
        await Entry.PublishAsync(value, token);

    [Benchmark]
    public async ValueTask Dispatch() => await Entry.PublishAsync(request, Token);

    [Benchmark]
    public async ValueTask DispatchAsync() => await Entry.PublishAsync(asynchronousRequest, Token);
}

public sealed record FoundatioNotice4(Counter Counter, Probe? Probe = null, bool Asynchronous = false);
[F.Handler(1)]
public sealed class FoundatioNotice4Handler1
{
    public ValueTask HandleAsync(FoundatioNotice4 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 1, token);
}
[F.Handler(2)]
public sealed class FoundatioNotice4Handler2
{
    public ValueTask HandleAsync(FoundatioNotice4 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 2, token);
}
[F.Handler(3)]
public sealed class FoundatioNotice4Handler3
{
    public ValueTask HandleAsync(FoundatioNotice4 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 3, token);
}
[F.Handler(4)]
public sealed class FoundatioNotice4Handler4
{
    public ValueTask HandleAsync(FoundatioNotice4 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 4, token);
}

[MemoryDiagnoser]
public class FoundatioNotification4 : FoundatioState
{
    private readonly Counter counter = new();
    private FoundatioNotice4 request = null!;
    public int CounterValue => counter.Value;
    public int CounterActive => counter.Active;
    private FoundatioNotice4 asynchronousRequest = null!;
    public int AsynchronousCounterValue => asynchronousRequest.Counter.Value;
    public int AsynchronousCounterActive => asynchronousRequest.Counter.Active;
    protected override void InitializeRequest()
    {
        request = new(counter, Probe);
        asynchronousRequest = new(new Counter(), Probe, Asynchronous: true);
    }

    public async ValueTask Invoke(FoundatioNotice4 value, CancellationToken token = default) =>
        await Entry.PublishAsync(value, token);

    [Benchmark]
    public async ValueTask Dispatch() => await Entry.PublishAsync(request, Token);

    [Benchmark]
    public async ValueTask DispatchAsync() => await Entry.PublishAsync(asynchronousRequest, Token);
}

public sealed record FoundatioNotice16(Counter Counter, Probe? Probe = null, bool Asynchronous = false);
[F.Handler(1)]
public sealed class FoundatioNotice16Handler1
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 1, token);
}
[F.Handler(2)]
public sealed class FoundatioNotice16Handler2
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 2, token);
}
[F.Handler(3)]
public sealed class FoundatioNotice16Handler3
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 3, token);
}
[F.Handler(4)]
public sealed class FoundatioNotice16Handler4
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 4, token);
}
[F.Handler(5)]
public sealed class FoundatioNotice16Handler5
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 5, token);
}
[F.Handler(6)]
public sealed class FoundatioNotice16Handler6
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 6, token);
}
[F.Handler(7)]
public sealed class FoundatioNotice16Handler7
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 7, token);
}
[F.Handler(8)]
public sealed class FoundatioNotice16Handler8
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 8, token);
}
[F.Handler(9)]
public sealed class FoundatioNotice16Handler9
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 9, token);
}
[F.Handler(10)]
public sealed class FoundatioNotice16Handler10
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 10, token);
}
[F.Handler(11)]
public sealed class FoundatioNotice16Handler11
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 11, token);
}
[F.Handler(12)]
public sealed class FoundatioNotice16Handler12
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 12, token);
}
[F.Handler(13)]
public sealed class FoundatioNotice16Handler13
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 13, token);
}
[F.Handler(14)]
public sealed class FoundatioNotice16Handler14
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 14, token);
}
[F.Handler(15)]
public sealed class FoundatioNotice16Handler15
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 15, token);
}
[F.Handler(16)]
public sealed class FoundatioNotice16Handler16
{
    public ValueTask HandleAsync(FoundatioNotice16 request, CancellationToken token) =>
        Work.Notify(request.Counter, request.Probe, request.Asynchronous, 16, token);
}

[MemoryDiagnoser]
public class FoundatioNotification16 : FoundatioState
{
    private readonly Counter counter = new();
    private FoundatioNotice16 request = null!;
    public int CounterValue => counter.Value;
    public int CounterActive => counter.Active;
    private FoundatioNotice16 asynchronousRequest = null!;
    public int AsynchronousCounterValue => asynchronousRequest.Counter.Value;
    public int AsynchronousCounterActive => asynchronousRequest.Counter.Active;
    protected override void InitializeRequest()
    {
        request = new(counter, Probe);
        asynchronousRequest = new(new Counter(), Probe, Asynchronous: true);
    }

    public async ValueTask Invoke(FoundatioNotice16 value, CancellationToken token = default) =>
        await Entry.PublishAsync(value, token);

    [Benchmark]
    public async ValueTask Dispatch() => await Entry.PublishAsync(request, Token);

    [Benchmark]
    public async ValueTask DispatchAsync() => await Entry.PublishAsync(asynchronousRequest, Token);
}

public sealed record FoundatioFeaturePathObservation(
    string Key, string Mode, int Handlers, int? CounterValue, int? Active,
    string? ExceptionType, string[] LeafExceptionTypes, string ActualType, string Method,
    Dictionary<string, object?> Parameters, int[] Events, int? Response, string? RequestType, object? Registration);

// This gate calls the compiled timed methods, not only the public Invoke helpers.
// It records native Publish exception shapes; no shape result exists until execution.
public static class FeaturesAdmission
{
    public static async Task<IReadOnlyList<FoundatioFeaturePathObservation>> ValidateTimingPathsAsync()
    {
        var rows = new List<FoundatioFeaturePathObservation>();
        await CheckVoid<FoundatioVoid0>("Void0", 0, static () => new(), static x => x.Dispatch(), static x => x.CounterValue, rows);
        await CheckVoid<FoundatioVoid1>("Void1", 1, static () => new(), static x => x.Dispatch(), static x => x.CounterValue, rows);
        await CheckVoid<FoundatioVoid3>("Void3", 3, static () => new(), static x => x.Dispatch(), static x => x.CounterValue, rows);
        await CheckVoid<FoundatioVoid5>("Void5", 5, static () => new(), static x => x.Dispatch(), static x => x.CounterValue, rows);
        await CheckScalar<FoundatioClosed>("Closed", static () => new(), static x => x.Dispatch(), rows);
        await CheckScalar<FoundatioOpen>("Open", static () => new(), static x => x.Dispatch(), rows);
        await CheckNotification<FoundatioNotification0>(0, static () => new(), static (x, _) => x.Dispatch(), static (x, _) => x.CounterValue, static (x, _) => x.CounterActive, rows);
        await CheckNotification<FoundatioNotification1>(1, static () => new(), static (x, asynchronous) => asynchronous ? x.DispatchAsync() : x.Dispatch(), static (x, asynchronous) => asynchronous ? x.AsynchronousCounterValue : x.CounterValue, static (x, asynchronous) => asynchronous ? x.AsynchronousCounterActive : x.CounterActive, rows);
        await CheckNotification<FoundatioNotification4>(4, static () => new(), static (x, asynchronous) => asynchronous ? x.DispatchAsync() : x.Dispatch(), static (x, asynchronous) => asynchronous ? x.AsynchronousCounterValue : x.CounterValue, static (x, asynchronous) => asynchronous ? x.AsynchronousCounterActive : x.CounterActive, rows);
        await CheckNotification<FoundatioNotification16>(16, static () => new(), static (x, asynchronous) => asynchronous ? x.DispatchAsync() : x.Dispatch(), static (x, asynchronous) => asynchronous ? x.AsynchronousCounterValue : x.CounterValue, static (x, asynchronous) => asynchronous ? x.AsynchronousCounterActive : x.CounterActive, rows);
        return rows;
    }

    private static async Task CheckVoid<T>(string key, int depth, Func<T> create,
        Func<T, ValueTask> timed, Func<T, int> counter,
        List<FoundatioFeaturePathObservation> rows) where T : FoundatioState
    {
        foreach (bool cancelled in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            if (cancelled)
                cancellation.Cancel();
            var probe = new Probe();
            var value = create();
            value.Probe = probe;
            value.Token = cancelled ? cancellation.Token : CancellationToken.None;
            value.Setup();
            try
            {
                var error = await Capture(() => timed(value));
                if (cancelled)
                {
                    Require(error is OperationCanceledException, key + ": cancellation classification");
                    Require(probe.Handlers == 0 && counter(value) == 0, key + ": cancelled work ran");
                }
                else
                {
                    Require(error is null, key + ": successful dispatch faulted");
                    Require(probe.Handlers == 1 && counter(value) == 1, key + ": void work count");
                    Require(probe.Events.SequenceEqual(Enumerable.Range(1, depth).Append(100)), key + ": entered order");
                }
                Record(value, "Dispatch", key + "/Dispatch", cancelled ? "pre-cancelled" : "success", probe, counter(value), null, error, rows);
            }
            finally
            {
                value.Cleanup();
            }
        }
    }

    private static async Task CheckScalar<T>(string key, Func<T> create,
        Func<T, ValueTask<int>> timed, List<FoundatioFeaturePathObservation> rows) where T : FoundatioState
    {
        foreach (bool cancelled in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            if (cancelled)
                cancellation.Cancel();
            var probe = new Probe();
            var value = create();
            value.Probe = probe;
            value.Token = cancelled ? cancellation.Token : CancellationToken.None;
            value.Setup();
            try
            {
                int? response = null;
                var error = await Capture(async () => { response = await timed(value); });
                if (cancelled)
                {
                    Require(error is OperationCanceledException, key + ": cancellation classification");
                    Require(probe.Handlers == 0, key + ": cancelled handler ran");
                }
                else
                {
                    Require(error is null && response == 42, key + ": response");
                    Require(probe.Handlers == 1 && probe.Events.SequenceEqual(new[] { 100 }), key + ": handler work");
                }
                Record(value, "Dispatch", key + "/Dispatch", cancelled ? "pre-cancelled" : "success", probe, null, null, error, rows, response);
            }
            finally
            {
                value.Cleanup();
            }
        }
    }

    private static async Task CheckNotification<T>(int handlers, Func<T> create,
        Func<T, bool, ValueTask> timed, Func<T, bool, int> counter, Func<T, bool, int> active,
        List<FoundatioFeaturePathObservation> rows) where T : FoundatioState
    {
        foreach (bool asynchronous in handlers == 0 ? new[] { false } : new[] { false, true })
        {
            foreach (string mode in handlers == 0 ? new[] { "success", "pre-cancelled" } : new[] { "success", "pre-cancelled", "handler-fault" })
            {
                using var cancellation = new CancellationTokenSource();
                if (mode == "pre-cancelled")
                    cancellation.Cancel();
                var probe = new Probe { Fail = mode == "handler-fault" };
                var value = create();
                value.Probe = probe;
                value.Token = mode == "pre-cancelled" ? cancellation.Token : CancellationToken.None;
                value.Setup();
                try
                {
                    var error = await Capture(() => timed(value, asynchronous));
                    int count = counter(value, asynchronous), activeCount = active(value, asynchronous);
                    Require(activeCount == 0, "Notification: active callback escaped");
                    if (mode == "success")
                    {
                        Require(error is null, "Notification: successful publish faulted");
                        Require(probe.Handlers == handlers && count == handlers, "Notification: count");
                        var ordered = Enumerable.Range(1, handlers).SelectMany(id => new[] { 100, id });
                        Require(probe.Events.SequenceEqual(ordered), "Notification: ordered work");
                    }
                    else if (mode == "pre-cancelled")
                    {
                        Require(probe.Handlers == 0 && count == 0 && probe.Events.Count == 0, "Notification: cancelled work ran");
                        // Empty publish may return without any handler observing cancellation.
                        Require(handlers == 0 ? error is null || IsCancellation(error) : error is not null && IsCancellation(error),
                            "Notification: cancellation classification");
                    }
                    else
                    {
                        Require(error is not null && Leaves(error).Any() && Leaves(error).All(leaf => leaf is ArithmeticException), "Notification: handler failure lost");
                        // Native Publish documents continuing the other handlers after failure.
                        Require(probe.Handlers == handlers && count == 0, "Notification: fault work count");
                        Require(probe.Events.SequenceEqual(Enumerable.Repeat(100, handlers)), "Notification: fault work trace");
                    }
                    Record(value, asynchronous ? "DispatchAsync" : "Dispatch", "Notification" + handlers + "/" + (asynchronous ? "DispatchAsync" : "Dispatch"),
                        mode, probe, count, activeCount, error, rows);
                }
                finally
                {
                    value.Cleanup();
                }
            }
        }
    }

    private static async ValueTask<Exception?> Capture(Func<ValueTask> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static bool IsCancellation(Exception error) => Leaves(error).Any() && Leaves(error).All(leaf => leaf is OperationCanceledException);
    private static IEnumerable<Exception> Leaves(Exception error) =>
        error is AggregateException aggregate ? aggregate.InnerExceptions.SelectMany(Leaves) : new[] { error };

    private static void Record(FoundatioState state, string method, string key, string mode, Probe probe, int? count, int? active,
        Exception? error, List<FoundatioFeaturePathObservation> rows, int? response = null) =>
        rows.Add(new(key, mode, probe.Handlers, count, active, error?.GetType().FullName,
            error is null ? [] : Leaves(error).Select(leaf => leaf.GetType().FullName ?? leaf.GetType().Name).ToArray(),
            state.GetType().FullName!, state.GetType().GetMethod(method)?.Name
                ?? throw new InvalidOperationException("Missing concrete feature method"),
            FoundatioSemanticSnapshot.Parameters(state), probe.Events.ToArray(), response,
            state.GetType().GetField("request", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(state)?.GetType().FullName,
            state.GetType().GetProperty("RegistrationDescriptors")?.GetValue(state)));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
