using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// Independent synchronous same-item reference; no SG, IR or legacy adapter.
internal static class CanonicalStreamFlowReference
{
    public static IAsyncEnumerable<TItem> FilterMap<TRequest, TItem, TFilter, TMap>(IServiceProvider services,
        TRequest request, Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start,
        CancellationToken token = default, ContractEvents? events = null)
        where TFilter : IStreamItemFilter<TRequest, TItem>
        where TMap : IStreamItemMap<TRequest, TItem>
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(start);
        return new Replay<TRequest, TItem, TFilter, TMap>(services, request, start, token, events);
    }

    private sealed class Replay<TRequest, TItem, TFilter, TMap>(IServiceProvider services, TRequest request,
        Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start, CancellationToken api, ContractEvents? events) : IAsyncEnumerable<TItem>
        where TFilter : IStreamItemFilter<TRequest, TItem>
        where TMap : IStreamItemMap<TRequest, TItem>
    {
        public IAsyncEnumerator<TItem> GetAsyncEnumerator(CancellationToken enumeration = default)
            => new Cursor<TRequest, TItem, TFilter, TMap>(services, request, start, api, enumeration, events);
    }

    private sealed class Cursor<TRequest, TItem, TFilter, TMap>(IServiceProvider services, TRequest request,
        Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start, CancellationToken api, CancellationToken enumeration,
        ContractEvents? events) : IAsyncEnumerator<TItem>
        where TFilter : IStreamItemFilter<TRequest, TItem>
        where TMap : IStreamItemMap<TRequest, TItem>
    {
        private IAsyncEnumerator<TItem>? _inner;
        // Interface references preserve the DI-owned box for mutable value-type services too.
        private IStreamItemFilter<TRequest, TItem>? _filter;
        private IStreamItemMap<TRequest, TItem>? _map;
        private CancellationTokenSource? _linked;
        private CancellationToken _token;
        private bool _started, _ended, _disposed, _hasCurrent;
        private int _busy;
        private TItem _current = default!;
        public TItem Current => _hasCurrent ? _current : throw new InvalidOperationException("Current is unavailable.");
        private void Enter()
        { if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("Await the preceding operation."); }
        public ValueTask<bool> MoveNextAsync()
        {
            Enter(); _hasCurrent = false;
            if (_ended || _disposed) { Volatile.Write(ref _busy, 0); return new(false); }
            return Move();
        }
        private async ValueTask<bool> Move()
        {
            try
            {
                if (!_started)
                {
                    _started = true;
                    _token = !api.CanBeCanceled ? enumeration : !enumeration.CanBeCanceled || api == enumeration ? api
                        : (_linked = CancellationTokenSource.CreateLinkedTokenSource(api, enumeration)).Token;
                    _token.ThrowIfCancellationRequested(); events?.Add("source", "start", request, _token);
                    _inner = start(request, _token).GetAsyncEnumerator(_token);
                }
                while (true)
                {
                    _token.ThrowIfCancellationRequested(); events?.Add("source", "move", request, _token);
                    if (!await _inner!.MoveNextAsync().ConfigureAwait(false))
                    { _ended = true; await Close().ConfigureAwait(false); return false; }
                    var item = _inner.Current;
                    _token.ThrowIfCancellationRequested();
                    if (_filter is null)
                    {
                        events?.Add("filter", "resolve", request, _token);
                        _filter = (IStreamItemFilter<TRequest, TItem>)services.GetRequiredService(typeof(TFilter));
                    }
                    events?.Add("filter", "accept", request, _token, service: _filter);
                    if (!_filter.Accept(request, item, _token)) continue;
                    _token.ThrowIfCancellationRequested();
                    if (_map is null)
                    {
                        events?.Add("map", "resolve", request, _token);
                        _map = (IStreamItemMap<TRequest, TItem>)services.GetRequiredService(typeof(TMap));
                    }
                    events?.Add("map", "map", request, _token, service: _map);
                    _current = _map.Map(request, item, _token); _hasCurrent = true; return true;
                }
            }
            catch
            {
                _ended = true; await Close().ConfigureAwait(false); throw;
            }
            finally { Volatile.Write(ref _busy, 0); }
        }
        public ValueTask DisposeAsync()
        {
            Enter(); _hasCurrent = false;
            if (_disposed) { Volatile.Write(ref _busy, 0); return default; }
            _disposed = _ended = true; return Dispose();
        }
        private async ValueTask Dispose()
        { try { await Close().ConfigureAwait(false); } finally { Volatile.Write(ref _busy, 0); } }
        private async ValueTask Close()
        {
            var inner = _inner; var linked = _linked; _inner = null; _linked = null;
            try
            { if (inner is not null) { events?.Add("source", "dispose", request, _token); await inner.DisposeAsync().ConfigureAwait(false); } }
            finally { linked?.Dispose(); }
        }
    }
}
