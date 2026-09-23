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
        var enumerator = $"StreamRoute{index}Enumerator";
        var enumerable = $"StreamRoute{index}Enumerable";
        // Typed enumerable: lightweight, no shared mutable state on the mediator.
        b.AppendLine($$"""
                private sealed class {{enumerable}}{{TypeParameters(route)}}(global::Zendiator.DependencyInjection.ZendiatorServiceResolver services, {{req}} request, global::System.Threading.CancellationToken apiToken) : global::System.Collections.Generic.IAsyncEnumerable<{{item}}>{{TypeConstraints(route)}}
                {
                    public global::System.Collections.Generic.IAsyncEnumerator<{{item}}> GetAsyncEnumerator(global::System.Threading.CancellationToken cancellationToken = default) => new {{enumerator}}{{tp}}(services, request, apiToken, cancellationToken);
                }
            """);
    }

    private static void EmitStreamEnumerator(StringBuilder b, EmissionRoute route, int index)
    {
        var tp = TypeParameters(route);
        var req = route.IsOpen ? route.RequestDisplay : Name(route.Request);
        var item = route.IsOpen ? route.ResponseDisplay : Name(route.Response);
        var enumerator = $"StreamRoute{index}Enumerator";
        // Typed enumerator: resolves pipeline once on first MoveNext, merges tokens only when different.
        b.AppendLine($$"""
                private sealed class {{enumerator}}{{TypeParameters(route)}} : global::System.Collections.Generic.IAsyncEnumerator<{{item}}>{{TypeConstraints(route)}}
                {
                    private readonly global::Zendiator.DependencyInjection.ZendiatorServiceResolver _services;
                    private readonly {{req}} _request;
                    private readonly global::System.Threading.CancellationToken _apiToken;
                    private readonly global::System.Threading.CancellationToken _enumToken;
                    private global::System.Threading.CancellationTokenSource? _linked;
                    private global::System.Collections.Generic.IAsyncEnumerator<{{item}}>? _inner;
                    private bool _started;
                    private bool _done;
                    public {{enumerator}}(global::Zendiator.DependencyInjection.ZendiatorServiceResolver services, {{req}} request, global::System.Threading.CancellationToken apiToken, global::System.Threading.CancellationToken enumToken)
                    {
                        _services = services;
                        _request = request;
                        _apiToken = apiToken;
                        _enumToken = enumToken;
                    }
                    public {{item}} Current => _inner != null ? _inner.Current : default!;
            """);
        // Steady-state MoveNext is a synchronous passthrough: awaiting the inner
        // enumerator here would allocate an async state machine per item.
        // Only the first call (pipeline startup) uses an async method, once per enumeration.
        b.AppendLine("""
                    public global::System.Threading.Tasks.ValueTask<bool> MoveNextAsync()
                    {
                        if (_done) return new global::System.Threading.Tasks.ValueTask<bool>(false);
                        if (!_started) return StartAndMoveNextAsync();
                        return _inner!.MoveNextAsync();
                    }
                    private async global::System.Threading.Tasks.ValueTask<bool> StartAndMoveNextAsync()
                    {
                        _started = true;
            """);
        if (route.Request.IsReferenceType)
            b.AppendLine("""            global::System.ArgumentNullException.ThrowIfNull(_request);""");
        b.AppendLine($$"""
                            var effective = MergeStreamTokens(_apiToken, _enumToken, out var linked);
                            _linked = linked;
                            if (effective.IsCancellationRequested) ThrowDispatchCancellation(effective);
                            global::System.Collections.Generic.IAsyncEnumerable<{{item}}> pipeline = new StreamRoute{{index}}Node0{{tp}}(_services).InvokeAsync(_request, effective);
                            _inner = pipeline.GetAsyncEnumerator(effective);
                        try
                        {
                            var ok = await _inner!.MoveNextAsync().ConfigureAwait(false);
                            if (!ok) _done = true;
                            return ok;
                        }
                        catch
                        {
                            _done = true;
                            throw;
                        }
                    }
                    public async global::System.Threading.Tasks.ValueTask DisposeAsync()
                    {
                        _done = true;
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
