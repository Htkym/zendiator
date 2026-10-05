using System.Text;

namespace Zendiator.SourceGenerator;

internal sealed partial class SourceEmitter
{
    private void EmitStreamTokenMerge(StringBuilder b)
    {
        // Shared token-merge helper: link only when both tokens are cancellable and different.
        if (_model.Routes.Streams.Count != 0)
        {
            b.AppendLine("""
                    private static global::System.Threading.CancellationToken MergeStreamTokens(global::System.Threading.CancellationToken apiToken, global::System.Threading.CancellationToken enumeratorToken, out global::System.Threading.CancellationTokenSource? linked)
                    {
                        linked = null;
                        if (!apiToken.CanBeCanceled) return enumeratorToken;
                        if (!enumeratorToken.CanBeCanceled) return apiToken;
                        if (apiToken.Equals(enumeratorToken)) return apiToken;
                        linked = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(apiToken, enumeratorToken);
                        return linked.Token;
                    }
                """);
        }
    }

    private static void EmitStreamEnumerable(StringBuilder b, EmissionRoute route, int index)
    {
        var tp = TypeParameters(route);
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var enumerable = $"StreamRoute{index}Enumerable";
        // Typed enumerable: lightweight, no shared mutable state on the mediator.
        // The creating thread's first enumeration reuses this instance, like a compiler-generated iterator.
        // Typed enumerator: resolves pipeline once on first MoveNext, merges tokens only when different.
        b.AppendLine($$"""
                private sealed class {{enumerable}}{{TypeParameters(route)}} : global::System.Collections.Generic.IAsyncEnumerable<{{item}}>, global::System.Collections.Generic.IAsyncEnumerator<{{item}}>{{TypeConstraints(route)}}
                {
                    private readonly global::Zendiator.DependencyInjection.ZendiatorServiceResolver<Zendiator> _services;
                    private readonly {{req}} _request;
                    private readonly global::System.Threading.CancellationToken _apiToken;
                    private readonly int _ownerThreadId;
                    private global::System.Threading.CancellationToken _enumToken;
                    private global::System.Threading.CancellationTokenSource? _linked;
                    private global::System.Collections.Generic.IAsyncEnumerator<{{item}}>? _inner;
                    private StreamEnumeratorState _state;
                    public {{enumerable}}(global::Zendiator.DependencyInjection.ZendiatorServiceResolver<Zendiator> services, {{req}} request, global::System.Threading.CancellationToken apiToken)
                    {
                        _services = services;
                        _request = request;
                        _apiToken = apiToken;
                        _ownerThreadId = global::System.Environment.CurrentManagedThreadId;
                    }
                    public global::System.Collections.Generic.IAsyncEnumerator<{{item}}> GetAsyncEnumerator(global::System.Threading.CancellationToken cancellationToken = default)
                    {
                        var enumerator = _state == StreamEnumeratorState.Unclaimed && _ownerThreadId == global::System.Environment.CurrentManagedThreadId
                            ? this : new {{enumerable}}{{tp}}(_services, _request, _apiToken);
                        enumerator._state = StreamEnumeratorState.NotStarted;
                        enumerator._enumToken = cancellationToken;
                        return enumerator;
                    }
                    public {{item}} Current => _inner != null ? _inner.Current : default!;
            """);
        // Steady-state MoveNext is a synchronous passthrough: awaiting the inner
        // enumerator here would allocate an async state machine per item.
        // Only the first call (pipeline startup) uses an async method, once per enumeration.
        b.AppendLine("""
                    public global::System.Threading.Tasks.ValueTask<bool> MoveNextAsync()
                    {
                        if (_state == StreamEnumeratorState.Forwarding) return _inner!.MoveNextAsync();
                        if (_state == StreamEnumeratorState.NotStarted) return StartAndMoveNextAsync();
                        return new global::System.Threading.Tasks.ValueTask<bool>(false);
                    }
                    private global::System.Threading.Tasks.ValueTask<bool> StartAndMoveNextAsync()
                    {
                        var task = StartCoreAsync();
                        return task.IsCompletedSuccessfully
                            ? new global::System.Threading.Tasks.ValueTask<bool>(task.Result)
                            : new global::System.Threading.Tasks.ValueTask<bool>(task);
                    }
                    private async global::System.Threading.Tasks.Task<bool> StartCoreAsync()
                    {
                        _state = StreamEnumeratorState.Forwarding;
                        try
                        {
            """);
        if (route.Request.IsReferenceType)
            b.AppendLine("""                global::System.ArgumentNullException.ThrowIfNull(_request, "_request");""");
        b.AppendLine($$"""
                            var effective = MergeStreamTokens(_apiToken, _enumToken, out _linked);
                            if (effective.IsCancellationRequested) ThrowDispatchCancellation(effective);
                            _inner = new StreamRoute{{index}}Node0{{tp}}(_services).InvokeAsync(_request, effective).GetAsyncEnumerator(effective);
                            var ok = await _inner.MoveNextAsync().ConfigureAwait(false);
                            if (!ok) _state = StreamEnumeratorState.Stopped;
                            return ok;
                        }
                        catch
                        {
                            _state = StreamEnumeratorState.Stopped;
                            throw;
                        }
                    }
                    public global::System.Threading.Tasks.ValueTask DisposeAsync()
                    {
                        if (_inner != null || _linked != null) return DisposeInnerAsync();
                        _state = StreamEnumeratorState.Stopped;
                        return default;
                    }
                    private async global::System.Threading.Tasks.ValueTask DisposeInnerAsync()
                    {
                        _state = StreamEnumeratorState.Stopped;
                        var inner = _inner;
                        _inner = null;
                        try
                        {
                            if (inner != null) await inner.DisposeAsync().ConfigureAwait(false);
                        }
                        finally
                        {
                            _linked?.Dispose();
                            _linked = null;
                        }
                    }
                }
            """);
    }
}
