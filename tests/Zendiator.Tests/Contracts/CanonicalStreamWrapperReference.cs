using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// C02 reference for F1 -> opaque W -> F2. W alone decides how often and with which inputs to Open.
internal static class CanonicalStreamWrapperReference
{
    public static IAsyncEnumerable<TItem> FilterWrapperFilter<TRequest, TItem, TBefore, TWrapper, TAfter>(
        IServiceProvider services, TRequest request,
        Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start,
        CancellationToken token = default, ContractEvents? events = null)
        where TBefore : IStreamItemFilter<TRequest, TItem>
        where TWrapper : IStreamWrapper<TRequest, TItem>
        where TAfter : IStreamItemFilter<TRequest, TItem>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(start);
        return new FilterReplay<TRequest, TItem, TAfter>(services, request, (outer, effective) =>
        {
            var wrapper = (IStreamWrapper<TRequest, TItem>)services.GetRequiredService(typeof(TWrapper));
            events?.Add("W", "wrap", outer, effective, service: wrapper);
            var upstream = new Upstream<TRequest, TItem, TBefore>(services, start, events);
            // Wrap receives the effective outer token. Reapplying it to a directly returned Open
            // would combine it with W's replacement token and change the upstream contract.
            return wrapper.Wrap(outer, upstream, effective).GetAsyncEnumerator();
        }, token, events, "F2");
    }

    private sealed class Upstream<TRequest, TItem, TFilter>(IServiceProvider services,
        Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start, ContractEvents? events)
        : IStreamUpstream<TRequest, TItem>
        where TFilter : IStreamItemFilter<TRequest, TItem>
    {
        public IAsyncEnumerable<TItem> Open(TRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            events?.Add("upstream", "open", request, token);
            return new FilterReplay<TRequest, TItem, TFilter>(services, request,
                (input, effective) => start(input, effective).GetAsyncEnumerator(effective), token, events, "F1");
        }
    }

    private sealed class FilterReplay<TRequest, TItem, TFilter>(IServiceProvider services, TRequest request,
        Func<TRequest, CancellationToken, IAsyncEnumerator<TItem>> start,
        CancellationToken api, ContractEvents? events, string stage) : IAsyncEnumerable<TItem>
        where TFilter : IStreamItemFilter<TRequest, TItem>
    {
        public IAsyncEnumerator<TItem> GetAsyncEnumerator(CancellationToken enumeration = default)
            => new FilterCursor<TRequest, TItem, TFilter>(services, request, start, api, enumeration, events, stage);
    }

    // One synchronous hook interval per side of the opaque boundary. No legacy adapter or IR.
    private sealed class FilterCursor<TRequest, TItem, TFilter>(IServiceProvider services, TRequest request,
        Func<TRequest, CancellationToken, IAsyncEnumerator<TItem>> start,
        CancellationToken api, CancellationToken enumeration, ContractEvents? events, string stage)
        : IAsyncEnumerator<TItem>
        where TFilter : IStreamItemFilter<TRequest, TItem>
    {
        private IAsyncEnumerator<TItem>? _inner;
        private IStreamItemFilter<TRequest, TItem>? _filter;
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
            Enter();
            _hasCurrent = false;
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
                    events?.Add(stage + ".source", "start", request, _token);
                    _inner = start(request, _token);
                }
                while (true)
                {
                    _token.ThrowIfCancellationRequested();
                    if (!await _inner!.MoveNextAsync().ConfigureAwait(false))
                    { _ended = true; await Close().ConfigureAwait(false); return false; }
                    var item = _inner.Current;
                    _token.ThrowIfCancellationRequested();
                    if (_filter is null)
                    {
                        events?.Add(stage, "resolve", request, _token);
                        _filter = (IStreamItemFilter<TRequest, TItem>)services.GetRequiredService(typeof(TFilter));
                    }
                    events?.Add(stage, "accept", request, _token, service: _filter);
                    if (!_filter.Accept(request, item, _token)) continue;
                    _current = item; _hasCurrent = true; return true;
                }
            }
            catch
            { _ended = true; await Close().ConfigureAwait(false); throw; }
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
                {
                    events?.Add(stage + ".source", "dispose", request, _token);
                    await inner.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally { linked?.Dispose(); }
        }
    }
}
