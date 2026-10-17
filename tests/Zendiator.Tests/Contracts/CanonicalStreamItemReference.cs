using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// Same-type F1 -> Map -> F2 in one Move boundary, without a wrapper or adapter.
internal static class CanonicalStreamItemReference
{
    public static IAsyncEnumerable<TItem> FilterMapFilter<TRequest, TItem, TFirst, TMap, TLast>(
        IServiceProvider services, TRequest request, Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start,
        CancellationToken token = default, ContractEvents? events = null)
        where TFirst : IStreamItemFilter<TRequest, TItem>
        where TMap : IStreamItemMap<TRequest, TItem>
        where TLast : IStreamItemFilter<TRequest, TItem>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(start);
        return new Replay<TRequest, TItem, TFirst, TMap, TLast>(services, request, start, token, events);
    }

    private sealed class Replay<TRequest, TItem, TFirst, TMap, TLast>(IServiceProvider services, TRequest request,
        Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start, CancellationToken api, ContractEvents? events)
        : IAsyncEnumerable<TItem>
        where TFirst : IStreamItemFilter<TRequest, TItem>
        where TMap : IStreamItemMap<TRequest, TItem>
        where TLast : IStreamItemFilter<TRequest, TItem>
    {
        public IAsyncEnumerator<TItem> GetAsyncEnumerator(CancellationToken enumeration = default)
            => new Cursor<TRequest, TItem, TFirst, TMap, TLast>(services, request, start, api, enumeration, events);
    }

    private sealed class Cursor<TRequest, TItem, TFirst, TMap, TLast>(IServiceProvider services, TRequest request,
        Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start,
        CancellationToken api, CancellationToken enumeration, ContractEvents? events) : IAsyncEnumerator<TItem>
        where TFirst : IStreamItemFilter<TRequest, TItem>
        where TMap : IStreamItemMap<TRequest, TItem>
        where TLast : IStreamItemFilter<TRequest, TItem>
    {
        private IAsyncEnumerator<TItem>? _inner;
        private IStreamItemFilter<TRequest, TItem>? _first, _last;
        private IStreamItemMap<TRequest, TItem>? _map;
        private CancellationTokenSource? _linked;
        private CancellationToken _token;
        private bool _started, _ended, _disposed, _hasCurrent;
        private int _busy;
        private TItem _current = default!;
        public TItem Current => _hasCurrent ? _current : throw new InvalidOperationException("Current is unavailable.");

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                throw new InvalidOperationException("Await the preceding operation.");
        }

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
                    _token.ThrowIfCancellationRequested();
                    events?.Add("source", "start", request, _token);
                    _inner = start(request, _token).GetAsyncEnumerator(_token);
                }
                while (true)
                {
                    _token.ThrowIfCancellationRequested();
                    if (!await _inner!.MoveNextAsync().ConfigureAwait(false))
                    { _ended = true; await Close().ConfigureAwait(false); return false; }
                    var item = _inner.Current;
                    _token.ThrowIfCancellationRequested();
                    if (_first is null)
                    {
                        events?.Add("F1", "resolve", request, _token);
                        _first = (IStreamItemFilter<TRequest, TItem>)services.GetRequiredService(typeof(TFirst));
                    }
                    events?.Add("F1", "accept", request, _token, service: _first);
                    if (!_first.Accept(request, item, _token)) continue;
                    _token.ThrowIfCancellationRequested();
                    if (_map is null)
                    {
                        events?.Add("Map", "resolve", request, _token);
                        _map = (IStreamItemMap<TRequest, TItem>)services.GetRequiredService(typeof(TMap));
                    }
                    events?.Add("Map", "map", request, _token, service: _map);
                    var mapped = _map.Map(request, item, _token);
                    _token.ThrowIfCancellationRequested();
                    if (_last is null)
                    {
                        events?.Add("F2", "resolve", request, _token);
                        _last = (IStreamItemFilter<TRequest, TItem>)services.GetRequiredService(typeof(TLast));
                    }
                    events?.Add("F2", "accept", request, _token, service: _last);
                    if (!_last.Accept(request, mapped, _token)) continue;
                    _current = mapped; _hasCurrent = true; return true;
                }
            }
            catch
            {
                _ended = true;
                // Close failure replaces the operation failure, just as a throwing finally does.
                await Close().ConfigureAwait(false);
                throw;
            }
            finally { Volatile.Write(ref _busy, 0); }
        }

        public ValueTask DisposeAsync()
        {
            Enter(); _hasCurrent = false;
            if (_disposed) { Volatile.Write(ref _busy, 0); return default; }
            _disposed = _ended = true;
            return Dispose();
        }

        private async ValueTask Dispose()
        {
            try { await Close().ConfigureAwait(false); }
            finally { Volatile.Write(ref _busy, 0); }
        }

        private async ValueTask Close()
        {
            var inner = _inner; var linked = _linked; _inner = null; _linked = null;
            try
            {
                if (inner is not null)
                { events?.Add("source", "dispose", request, _token); await inner.DisposeAsync().ConfigureAwait(false); }
            }
            finally { linked?.Dispose(); }
        }
    }
}
