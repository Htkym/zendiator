using System.Globalization;
using System.Threading.Tasks.Sources;

namespace Zendiator.Tests.Contracts;

// Test-only trace shared by canonical controls and future generated backend adapters.
internal sealed record ContractEvent(string Family, int Branch, int Invocation, string Stage, string Kind,
    string Request, int Token, int Error, int Service, string? Resolution, string? Ambient, string Culture, string? Context, string? Detail);

internal sealed class ContractEvents(string family, int invocation, int branch = 0)
{
    public static readonly AsyncLocal<string?> Ambient = new();
    private readonly List<ContractEvent> _events = [];
    private readonly Dictionary<object, int> _objects = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CancellationToken, int> _tokens = [];
    public void Add<R>(string stage, string kind, R request, CancellationToken token = default,
        Exception? error = null, string? detail = null, object? service = null, string? resolution = null)
    {
        lock (_events)
        {
            var requestId = typeof(R).IsValueType ? "value:" + request : request is null ? "null" : "ref:" + Id(request);
            if (token.CanBeCanceled && !_tokens.ContainsKey(token)) _tokens.Add(token, _tokens.Count + 1);
            _events.Add(new(family, branch, invocation, stage, kind, requestId,
                token.CanBeCanceled ? _tokens[token] : 0, error is null ? 0 : Id(error), service is null ? 0 : Id(service), resolution, Ambient.Value,
                CultureInfo.CurrentCulture.Name, SynchronizationContext.Current?.GetType().Name, detail));
        }
    }
    private int Id(object value)
    { if (!_objects.TryGetValue(value, out var id)) _objects.Add(value, id = _objects.Count + 1); return id; }
    public ContractEvent[] Snapshot() { lock (_events) return [.. _events]; }
    public string[] Phases() => Snapshot().Select(e => e.Stage + "." + e.Kind).ToArray();
}

internal sealed record AwaitObservation<T>(bool CallsiteThrow, bool Completed, bool Canceled, bool Faulted,
    T? Value, Exception? Error);
internal sealed record VoidAwaitObservation(bool CallsiteThrow, bool Completed, bool Canceled, bool Faulted, Exception? Error);

internal static class ContractAwait
{
    public static async Task<VoidAwaitObservation> Observe(Func<ValueTask> call, Action? release = null)
    {
        ValueTask value;
        try { value = call(); }
        catch (Exception error) { return new(true, false, false, false, error); }
        var completed = value.IsCompletedSuccessfully; var canceled = value.IsCanceled; var faulted = value.IsFaulted;
        release?.Invoke();
        try { await value; return new(false, completed, canceled, faulted, null); }
        catch (Exception error) { return new(false, completed, canceled, faulted, error); }
    }
    public static async Task<AwaitObservation<T>> Observe<T>(Func<ValueTask<T>> call, Action? release = null)
    {
        ValueTask<T> value;
        try { value = call(); }
        catch (Exception error) { return new(true, false, false, false, default, error); }
        var completed = value.IsCompletedSuccessfully; var canceled = value.IsCanceled; var faulted = value.IsFaulted;
        release?.Invoke();
        try { return new(false, completed, canceled, faulted, await value, null); }
        catch (Exception error) { return new(false, completed, canceled, faulted, default, error); }
    }
}

// Real suspension and one GetResult; GetStatus is deliberately safe to observe repeatedly.
internal sealed class ContractOnce<T> : IValueTaskSource<T>, IValueTaskSource
{
    private ManualResetValueTaskSourceCore<T> _core = new() { RunContinuationsAsynchronously = true };
    private int _consumptions;
    public int Consumptions => Volatile.Read(ref _consumptions);
    public ValueTask<T> Task => new(this, _core.Version);
    public ValueTask Void => new(this, _core.Version);
    public void Complete(T value) => _core.SetResult(value);
    public void Fail(Exception error) => _core.SetException(error);
    public T GetResult(short token)
    {
        if (Interlocked.Increment(ref _consumptions) != 1) throw new InvalidOperationException("Double consumption.");
        return _core.GetResult(token);
    }
    void IValueTaskSource.GetResult(short token) => GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(continuation, state, token, flags);
}
