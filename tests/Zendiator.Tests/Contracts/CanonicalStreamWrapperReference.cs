using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// F1 -> opaque W -> F2. W decides how often and with which inputs to open upstream.
internal static class CanonicalStreamWrapperReference
{
    // Async intervals remain on their own side of the opaque wrapper.
    public static IAsyncEnumerable<TItem> AsyncFilterWrapperMap<TRequest, TItem, TBefore, TWrapper, TAfter>(
        IServiceProvider services, TRequest request, Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start,
        CancellationToken api = default)
        where TBefore : IAsyncStreamItemFilter<TRequest, TItem>
        where TWrapper : IStreamWrapper<TRequest, TItem>
        where TAfter : IAsyncStreamItemMap<TRequest, TItem>
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(start);
        return Run();
        async IAsyncEnumerable<TItem> Run([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken enumeration = default)
        {
            CancellationTokenSource? linked = null; IAsyncEnumerator<TItem>? source = null;
            IAsyncStreamItemMap<TRequest, TItem>? map = null;
            try
            {
                var token = !api.CanBeCanceled ? enumeration : !enumeration.CanBeCanceled || api == enumeration ? api
                    : (linked = CancellationTokenSource.CreateLinkedTokenSource(api, enumeration)).Token;
                token.ThrowIfCancellationRequested();
                var wrapper = (IStreamWrapper<TRequest, TItem>)services.GetRequiredService(typeof(TWrapper));
                source = wrapper.Wrap(request, new AsyncUpstream<TRequest, TItem, TBefore>(services, start), token).GetAsyncEnumerator();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (!await source.MoveNextAsync().ConfigureAwait(false)) yield break;
                    var item = source.Current; token.ThrowIfCancellationRequested();
                    map ??= (IAsyncStreamItemMap<TRequest, TItem>)services.GetRequiredService(typeof(TAfter));
                    yield return await map.MapAsync(request, item, token).ConfigureAwait(false);
                }
            }
            finally
            {
                var inner = source; source = null;
                try { if (inner is not null) await inner.DisposeAsync().ConfigureAwait(false); }
                finally { linked?.Dispose(); }
            }
        }
    }
    private sealed class AsyncUpstream<TRequest, TItem, TFilter>(IServiceProvider services,
        Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start) : IStreamUpstream<TRequest, TItem>
        where TFilter : IAsyncStreamItemFilter<TRequest, TItem>
    {
        public IAsyncEnumerable<TItem> Open(TRequest request, CancellationToken api)
        {
            ArgumentNullException.ThrowIfNull(request); return Run();
            async IAsyncEnumerable<TItem> Run([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken enumeration = default)
            {
                CancellationTokenSource? linked = null; IAsyncEnumerator<TItem>? source = null;
                IAsyncStreamItemFilter<TRequest, TItem>? filter = null;
                try
                {
                    var token = !api.CanBeCanceled ? enumeration : !enumeration.CanBeCanceled || api == enumeration ? api
                        : (linked = CancellationTokenSource.CreateLinkedTokenSource(api, enumeration)).Token;
                    token.ThrowIfCancellationRequested(); source = start(request, token).GetAsyncEnumerator(token);
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!await source.MoveNextAsync().ConfigureAwait(false)) yield break;
                        var item = source.Current; token.ThrowIfCancellationRequested();
                        filter ??= (IAsyncStreamItemFilter<TRequest, TItem>)services.GetRequiredService(typeof(TFilter));
                        if (await filter.AcceptAsync(request, item, token).ConfigureAwait(false)) yield return item;
                    }
                }
                finally
                {
                    var inner = source; source = null;
                    try { if (inner is not null) await inner.DisposeAsync().ConfigureAwait(false); }
                    finally { linked?.Dispose(); }
                }
            }
        }
    }

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
