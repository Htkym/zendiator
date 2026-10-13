using System.Globalization;

namespace Zendiator.Generator.Tests;

// One synchronous wrapper fixture. This observes the actual generated/reference cursor and typed Open.
// Scope forwarding returns the original ValueTask; it adds no async adapter or token merge.
public sealed class CanonicalStreamWrapperEventProbe
{
    public CancellationToken ApiToken, FirstToken, SecondToken;
    public int[] Input = [1, 2];
    public int Starts, Moves, Reads, Disposals;
    private readonly AsyncLocal<Scope?> _active = new();
    private readonly Dictionary<int, Scope> _scopes = [];
    private readonly Dictionary<object, int> _requests = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CancellationToken, int> _tokens = [];
    private readonly List<StreamSeedEvent> _events = [];
    private int _nextInvocation;
    internal int LastOuterInvocation { get; private set; }
    internal StreamSeedEvent[] Snapshot() => [.. _events];
    internal sealed class Scope(int id, int parent, object request, CancellationToken token)
    {
        public readonly int Id = id, Parent = parent;
        public readonly object Request = request;
        public readonly CancellationToken Token = token;
        public int SourceSequence = -1, OutputSequence = -1;
        public int RelatedInvocation;
        public int? RelatedSequence;
    }
    private Scope Current => _active.Value ?? throw new InvalidOperationException("Missing observed invocation.");
    private Scope NewScope(int parent, object request, CancellationToken token)
    {
        var scope = new Scope(++_nextInvocation, parent, request, token); _scopes.Add(scope.Id, scope);
        if (parent == 0) LastOuterInvocation = scope.Id;
        return scope;
    }
    private int RequestId(object request)
    { if (!_requests.TryGetValue(request, out var id)) _requests.Add(request, id = _requests.Count + 1); return id; }
    private int TokenId(CancellationToken token)
    {
        if (!token.CanBeCanceled) return 0;
        if (!_tokens.TryGetValue(token, out var id)) _tokens.Add(token, id = _tokens.Count + 1);
        return id;
    }
    private void Add(Scope scope, CanonicalStreamProbeEvent value, int related = 0,
        int? relatedSequence = null, CancellationToken? enumeratorToken = null)
    {
        if (!ReferenceEquals(scope.Request, value.Request) || scope.Token != value.Token)
            throw new InvalidOperationException("An invocation received another request/token.");
        _events.Add(new(value.Kind, value.Stage, scope.Id, RequestId(scope.Request), TokenId(value.Token),
            value.Sequence, StreamEventItems.Int32(value.Input), StreamEventItems.Int32(value.Output), value.Result, 0, null,
            scope.Parent, related, relatedSequence, enumeratorToken.HasValue ? TokenId(enumeratorToken.Value) : null));
    }
    private void Emit(string kind, string stage, object request, CancellationToken token,
        int? sequence = null, object? input = null, object? output = null, bool? result = null)
        => Add(Current, new(kind, stage, request, token, sequence, input, output, result),
            stage == "F2" ? Current.RelatedInvocation : 0, stage == "F2" ? Current.RelatedSequence : null);
    private ScopeLease Enter(Scope scope)
    { var previous = _active.Value; _active.Value = scope; return new(this, previous); }
    private sealed class ScopeLease(CanonicalStreamWrapperEventProbe owner, Scope? previous) : IDisposable
    { public void Dispose() => owner._active.Value = previous; }

    public void Resolve(string role) => Emit("Resolve", role, Current.Request, Current.Token);
    public void Wrap(object request, CancellationToken token) => Emit("WrapperWrap", "W", request, token);
    public void WrapperBefore(object request, CancellationToken token) => Emit("WrapperBefore", "W", request, token);
    public void WrapperFinally(object request, CancellationToken token) => Emit("WrapperFinally", "W", request, token);
    public void WrapperDrop(object request, CancellationToken token, int item, int upstreamInvocation)
        => Add(Current, new("WrapperDrop", "W", request, token, Input: item), upstreamInvocation, _scopes[upstreamInvocation].SourceSequence);
    public void WrapperItem(object request, CancellationToken token, int item, int upstreamInvocation)
    {
        var sequence = ++Current.OutputSequence;
        Current.RelatedInvocation = upstreamInvocation; Current.RelatedSequence = _scopes[upstreamInvocation].SourceSequence;
        Add(Current, new("WrapperItem", "W", request, token, sequence, Input: item, Output: item),
            upstreamInvocation, _scopes[upstreamInvocation].SourceSequence);
    }
    public bool First<T>(object request, T item, CancellationToken token)
    {
        var accepted = Convert.ToInt32(item, CultureInfo.InvariantCulture) > 0;
        Emit("Filter", "F1", request, token, Current.SourceSequence, item, result: accepted); return accepted;
    }
    public bool Last<T>(object request, T item, CancellationToken token)
    {
        var accepted = Convert.ToInt32(item, CultureInfo.InvariantCulture) != 20;
        Emit("Filter", "F2", request, token, Current.OutputSequence, item, result: accepted); return accepted;
    }
    public T Map<T>(object request, T item, CancellationToken token)
    {
        var mapped = (T)Convert.ChangeType(checked(Convert.ToInt32(item, CultureInfo.InvariantCulture) * 2), typeof(T), CultureInfo.InvariantCulture)!;
        Emit("Map", "Map", request, token, input: item, output: mapped); return mapped;
    }
    public IAsyncEnumerable<T> Start<T>(object request, CancellationToken token)
    {
        Emit("HandlerStart", "H", request, token); Starts++;
        return new Source<T>(this, Current, request, token);
    }
    private sealed class Source<T>(CanonicalStreamWrapperEventProbe p, Scope scope, object request, CancellationToken start)
        : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        private int _index = -1;
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken token = default)
        { p.Add(scope, new("InnerGet", "H", request, token)); return this; }
        public T Current
        {
            get
            {
                p.Reads++; scope.SourceSequence = _index;
                var value = (T)Convert.ChangeType(p.Input[_index], typeof(T), CultureInfo.InvariantCulture)!;
                p.Add(scope, new("InnerCurrent", "H", request, start, _index, Output: value)); return value;
            }
        }
        public ValueTask<bool> MoveNextAsync()
        { p.Moves++; _index++; p.Add(scope, new("InnerMoveNext", "H", request, start, _index)); return new(_index < p.Input.Length); }
        public ValueTask DisposeAsync()
        {
            p.Disposals++; p.Add(scope, new("InnerDisposeStart", "H", request, start));
            p.Add(scope, new("InnerDisposeEnd", "H", request, start)); return default;
        }
    }

    internal TrackedEnumerable ObserveOuter(IAsyncEnumerable<int> inner, object request, CancellationToken api)
        => new(this, inner, request, api, null);
    public TrackedEnumerable ObserveOpen<TRequest, TUpstream>(TRequest request, TUpstream upstream, CancellationToken token)
        where TUpstream : IStreamUpstream<TRequest, int>
    {
        var child = NewScope(Current.Id, request!, token);
        Add(child, new("UpstreamOpen", "W", request, token));
        // Preserve the real typed Open and its lazy enumerable, with no extra call or token application.
        var inner = upstream.Open(request, token);
        return new(this, inner, request!, token, child);
    }
    public sealed class TrackedEnumerable : IAsyncEnumerable<int>
    {
        private readonly CanonicalStreamWrapperEventProbe _owner;
        private readonly IAsyncEnumerable<int> _inner;
        private readonly object _request;
        private readonly CancellationToken _api;
        private readonly Scope? _child;
        internal TrackedEnumerable(CanonicalStreamWrapperEventProbe owner, IAsyncEnumerable<int> inner, object request, CancellationToken api, Scope? child)
        { _owner = owner; _inner = inner; _request = request; _api = api; _child = child; }
        public int Invocation => _child?.Id ?? throw new InvalidOperationException("Only an Open has a reserved child invocation.");
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default)
        {
            var scope = _child ?? _owner.NewScope(0, _request, _api);
            using var lease = _owner.Enter(scope);
            _owner.Add(scope, new("EnumeratorAcquire", scope.Parent == 0 ? "outer" : "upstream", _request, _api), enumeratorToken: token);
            return new TrackedCursor(_owner, _inner.GetAsyncEnumerator(token), scope);
        }
    }
    private sealed class TrackedCursor(CanonicalStreamWrapperEventProbe owner, IAsyncEnumerator<int> inner, Scope scope) : IAsyncEnumerator<int>
    {
        public int Current { get { using var lease = owner.Enter(scope); return inner.Current; } }
        public ValueTask<bool> MoveNextAsync() { using var lease = owner.Enter(scope); return inner.MoveNextAsync(); }
        public ValueTask DisposeAsync()
        {
            using var lease = owner.Enter(scope);
            owner.Add(scope, new("EnumeratorDispose", scope.Parent == 0 ? "outer" : "upstream", scope.Request, scope.Token));
            var value = inner.DisposeAsync();
            if (value.IsCompletedSuccessfully)
                owner.Add(scope, new("EnumeratorDisposed", scope.Parent == 0 ? "outer" : "upstream", scope.Request, scope.Token));
            return value;
        }
    }
    internal void ConsumerYield(int outerInvocation, int item)
    {
        var scope = _scopes[outerInvocation];
        Add(scope, new("Yield", "consumer", scope.Request, scope.Token, scope.OutputSequence, Output: item),
            scope.RelatedInvocation, scope.RelatedSequence);
    }
}
