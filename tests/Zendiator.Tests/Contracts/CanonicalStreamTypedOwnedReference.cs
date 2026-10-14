using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// Test-only independent ordinary iterator with cursor-owned typed source/hook leases.
internal static class CanonicalStreamTypedOwnedReference
{
    public static IAsyncEnumerable<TOut> Map<R, TIn, TOut, H, K, HF, KF>(IServiceProvider services, R request)
        where R : IStreamRequest<TIn> where H : class, IStreamRequestHandler<R, TIn>, IAsyncDisposable
        where K : class, IStreamItemMap<R, TIn, TOut>, IAsyncDisposable
        where HF : class, IOwnedSessionFactory<R, H> where KF : class, IOwnedSessionFactory<R, K> =>
        new Envelope<R, TIn, TOut, H, K>(services, request, default,
            (p, r, t) => p.GetRequiredService<KF>().Create(r, t), (p, r, t) => p.GetRequiredService<HF>().Create(r, t),
            (h, r, t) => h.HandleAsync(r, t).GetAsyncEnumerator(t), (k, r, item, t) => new(k.Map(r, item, t)));

    public static IAsyncEnumerable<TOut> MapWrapperMap<R, TIn, TMid, TOut, H, A, W, B, HF, AF, WF, BF>(IServiceProvider services, R request)
        where R : IStreamRequest<TIn> where H : class, IStreamRequestHandler<R, TIn>, IAsyncDisposable
        where A : class, IStreamItemMap<R, TIn, TMid>, IAsyncDisposable where W : class, IStreamWrapper<R, TMid>, IAsyncDisposable
        where B : class, IAsyncStreamItemMap<R, TMid, TOut>, IAsyncDisposable
        where HF : class, IOwnedSessionFactory<R, H> where AF : class, IOwnedSessionFactory<R, A>
        where WF : class, IOwnedSessionFactory<R, W> where BF : class, IOwnedSessionFactory<R, B> =>
        new Envelope<R, TMid, TOut, W, B>(services, request, default,
            (p, r, t) => p.GetRequiredService<BF>().Create(r, t), (p, r, t) => p.GetRequiredService<WF>().Create(r, t),
            (w, r, t) => w.Wrap(r, new Upstream<R, TIn, TMid, H, A, HF, AF>(services), t).GetAsyncEnumerator(),
            (b, r, item, t) => b.MapAsync(r, item, t));

    private sealed class Upstream<R, TIn, TOut, H, K, HF, KF>(IServiceProvider services) : IStreamUpstream<R, TOut>
        where R : IStreamRequest<TIn> where H : class, IStreamRequestHandler<R, TIn>, IAsyncDisposable
        where K : class, IStreamItemMap<R, TIn, TOut>, IAsyncDisposable
        where HF : class, IOwnedSessionFactory<R, H> where KF : class, IOwnedSessionFactory<R, K>
    {
        public IAsyncEnumerable<TOut> Open(R request, CancellationToken token) =>
            new Envelope<R, TIn, TOut, H, K>(services, request, token,
                (p, r, t) => p.GetRequiredService<KF>().Create(r, t), (p, r, t) => p.GetRequiredService<HF>().Create(r, t),
                (h, r, t) => h.HandleAsync(r, t).GetAsyncEnumerator(t), (k, r, item, t) => new(k.Map(r, item, t)));
    }
    private sealed class Envelope<R, TIn, TOut, S, K>(IServiceProvider services, R request, CancellationToken api,
        Func<IServiceProvider, R, CancellationToken, K> acquireHook, Func<IServiceProvider, R, CancellationToken, S> acquireSource,
        Func<S, R, CancellationToken, IAsyncEnumerator<TIn>> start, Func<K, R, TIn, CancellationToken, ValueTask<TOut>> map) : IAsyncEnumerable<TOut>
        where S : class, IAsyncDisposable where K : class, IAsyncDisposable
    {
        public IAsyncEnumerator<TOut> GetAsyncEnumerator(CancellationToken token = default) =>
            new Cursor(services, request, api, token, acquireHook, acquireSource, start, map);
        private sealed class Cursor : IAsyncEnumerator<TOut>
        {
            private readonly IServiceProvider _services; private readonly R _request; private readonly CancellationToken _api, _enumeration;
            private readonly Func<IServiceProvider, R, CancellationToken, K> _hookFactory; private readonly Func<IServiceProvider, R, CancellationToken, S> _sourceFactory;
            private readonly Func<S, R, CancellationToken, IAsyncEnumerator<TIn>> _start; private readonly Func<K, R, TIn, CancellationToken, ValueTask<TOut>> _map;
            private readonly IAsyncEnumerator<TOut> _body; private IAsyncEnumerator<TIn>? _source; private S? _receiver; private K? _hook; private CancellationTokenSource? _linked;
            private bool _started, _ended, _disposed;
            public Cursor(IServiceProvider provider, R value, CancellationToken apiToken, CancellationToken enumeration,
                Func<IServiceProvider, R, CancellationToken, K> hookFactory, Func<IServiceProvider, R, CancellationToken, S> sourceFactory,
                Func<S, R, CancellationToken, IAsyncEnumerator<TIn>> source, Func<K, R, TIn, CancellationToken, ValueTask<TOut>> mapper)
            { _services = provider; _request = value; _api = apiToken; _enumeration = enumeration; _hookFactory = hookFactory; _sourceFactory = sourceFactory; _start = source; _map = mapper; _body = Items().GetAsyncEnumerator(); }
            public TOut Current => _body.Current;
            private async IAsyncEnumerable<TOut> Items()
            {
                var token = !_api.CanBeCanceled ? _enumeration : !_enumeration.CanBeCanceled || _api == _enumeration ? _api
                    : (_linked = CancellationTokenSource.CreateLinkedTokenSource(_api, _enumeration)).Token;
                token.ThrowIfCancellationRequested(); _hook = _hookFactory(_services, _request, token) ?? throw new InvalidOperationException();
                token.ThrowIfCancellationRequested(); _receiver = _sourceFactory(_services, _request, token) ?? throw new InvalidOperationException();
                _source = _start(_receiver, _request, token); _started = true;
                while (await _source.MoveNextAsync().ConfigureAwait(false))
                { var item = _source.Current; token.ThrowIfCancellationRequested(); yield return await _map(_hook, _request, item, token).ConfigureAwait(false); }
            }
            public async ValueTask<bool> MoveNextAsync()
            {
                if (_ended) return false;
                try { var result = await _body.MoveNextAsync().ConfigureAwait(false); _ended = !result; return result; }
                catch { _ended = true; if (!_started) await Release().ConfigureAwait(false); throw; }
            }
            public async ValueTask DisposeAsync()
            {
                if (_disposed) return; _ended = _disposed = true;
                try { await _body.DisposeAsync().ConfigureAwait(false); }
                finally { await Release().ConfigureAwait(false); }
            }
            private async ValueTask Release()
            {
                var source = _source; var receiver = _receiver; var hook = _hook; var linked = _linked; _source = null; _receiver = null; _hook = null; _linked = null;
                try { if (source != null) await source.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    try { if (receiver != null) await receiver.DisposeAsync().ConfigureAwait(false); }
                    finally { try { if (hook != null) await hook.DisposeAsync().ConfigureAwait(false); } finally { linked?.Dispose(); } }
                }
            }
        }
    }
}
