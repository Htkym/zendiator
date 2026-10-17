using Zendiator.Tests.Contracts;

namespace Zendiator.Generator.Tests;

public sealed class OpenAsyncRuntimeItem { public int Number { get; set; } }
internal sealed record OpenAsyncServiceEvent(string Kind, string Role, int Service);

// Test-only dependency of actual DI-constructed generic consumer services.
public sealed class CanonicalOpenAsyncRuntimeProbe<T> where T : class, new()
{
    internal object Request = null!;
    internal T Input = null!, Mapped = null!, Distractor = null!, SourceCurrent = null!;
    internal readonly ContractOnce<bool> FilterGate = new();
    internal readonly ContractOnce<T> MapGate = new();
    internal readonly TaskCompletionSource FilterEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource MapEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly List<StreamSeedEvent> Events = [];
    internal readonly List<OpenAsyncServiceEvent> ServiceEvents = [];
    private readonly Dictionary<object, (string Role, int Id)> _services = new(ReferenceEqualityComparer.Instance);
    internal Type[] CreatedTypes => _services.Keys.Select(service => service.GetType()).ToArray();
    private readonly HashSet<object> _released = new(ReferenceEqualityComparer.Instance);
    internal CancellationToken ExecutionToken;
    internal int Starts, Moves, Reads, Disposals, LastCalls;

    public void Created(object service, string role)
    {
        _services.Add(service, (role, _services.Count + 1)); ServiceEvents.Add(new("Created", role, _services.Count));
        Event("Resolve", role); // Logical invocation context; constructors have no request/token arguments.
    }
    public void Released(object service)
    {
        if (!_released.Add(service)) throw new InvalidOperationException("Service disposed twice.");
        var (role, id) = _services[service]; ServiceEvents.Add(new("Disposed", role, id)); Event("ServiceDisposed", role);
    }
    private void Called(object service, object request, CancellationToken token)
    {
        if (!ReferenceEquals(request, Request) || token != ExecutionToken || _released.Contains(service))
            throw new InvalidOperationException("Request/token/service ownership changed.");
        var (role, id) = _services[service]; ServiceEvents.Add(new("Called", role, id));
    }
    internal void Event(string kind, string stage, int? input = null, int? output = null, bool? result = null,
        bool callbackToken = false, Exception? error = null) => Events.Add(new(kind, stage, 1, 1, callbackToken ? 1 : 0,
            null, input, output, result, error == null ? 0 : 1, error?.GetType().FullName));
    public IAsyncEnumerable<T> Start(object service, object request, CancellationToken token)
    {
        ExecutionToken = token; Called(service, request, token); Starts++; Event("HandlerStart", "H", callbackToken: true); return new Source(this);
    }
    public ValueTask<bool> First(object service, object request, T item, CancellationToken token)
    {
        Called(service, request, token);
        if (!ReferenceEquals(item, Input)) throw new InvalidOperationException("Filter input changed.");
        Event("Filter", "F1", input: 1, callbackToken: true); FilterEntered.TrySetResult(); return FilterGate.Task;
    }
    public ValueTask<T> Map(object service, object request, T item, CancellationToken token)
    {
        Called(service, request, token);
        if (!ReferenceEquals(item, Input)) throw new InvalidOperationException("Suspended item was lost or Current read twice.");
        Event("Map", "Map", input: 1, callbackToken: true); MapEntered.TrySetResult(); return MapGate.Task;
    }
    public bool Last(object service, object request, T item, CancellationToken token)
    {
        Called(service, request, token);
        if (!ReferenceEquals(item, Mapped)) throw new InvalidOperationException("Mapped item was not retained.");
        LastCalls++; Event("Filter", "F2", input: 2, result: true, callbackToken: true); return true;
    }
    private sealed class Source(CanonicalOpenAsyncRuntimeProbe<T> p) : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        private int _index;
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default)
        {
            if (token != p.ExecutionToken) throw new InvalidOperationException("Source enumeration token changed.");
            p.Event("EnumeratorAcquired", "H", callbackToken: true); return this;
        }
        public T Current
        {
            get
            {
                p.Reads++; p.Event("InnerCurrent", "H", output: ReferenceEquals(p.SourceCurrent, p.Input) ? 1 : 9, callbackToken: true);
                return p.SourceCurrent;
            }
        }
        public ValueTask<bool> MoveNextAsync()
        { p.Moves++; var result = _index++ == 0; p.Event("InnerMove", "H", result: result, callbackToken: true); return new(result); }
        public ValueTask DisposeAsync()
        { p.Disposals++; p.Event("InnerDisposeStart", "H", callbackToken: true); p.Event("InnerDisposeEnd", "H", callbackToken: true); return default; }
    }
}
