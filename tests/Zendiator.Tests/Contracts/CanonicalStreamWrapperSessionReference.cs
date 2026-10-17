using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// Independent ordinary item iterators enclosed by cursor-owned leases. W alone opens/owns upstream cursors.
internal static class CanonicalStreamWrapperSessionReference
{
    public static IAsyncEnumerable<int> Create<R, H, I, W, O, HF, IF, WF, OF>(IServiceProvider services, R request)
        where R : IStreamRequest<int> where H : class, IStreamRequestHandler<R, int>, IAsyncDisposable
        where I : class, IStreamItemMap<R, int>, IAsyncDisposable where W : class, IStreamWrapper<R, int>, IAsyncDisposable
        where O : class, IStreamItemMap<R, int>, IAsyncDisposable where HF : class, IOwnedSessionFactory<R, H>
        where IF : class, IOwnedSessionFactory<R, I> where WF : class, IOwnedSessionFactory<R, W> where OF : class, IOwnedSessionFactory<R, O> =>
        new Envelope<R, W, O, WF, OF>(services, request, default,
            (wrapper, r, t) => wrapper.Wrap(r, new Upstream<R, H, I, HF, IF>(services), t).GetAsyncEnumerator());
    private sealed class Upstream<R, H, I, HF, IF>(IServiceProvider services) : IStreamUpstream<R, int>
        where R : IStreamRequest<int> where H : class, IStreamRequestHandler<R, int>, IAsyncDisposable
        where I : class, IStreamItemMap<R, int>, IAsyncDisposable where HF : class, IOwnedSessionFactory<R, H> where IF : class, IOwnedSessionFactory<R, I>
    {
        public IAsyncEnumerable<int> Open(R request, CancellationToken token) =>
            new Envelope<R, H, I, HF, IF>(services, request, token, (handler, r, t) => handler.HandleAsync(r, t).GetAsyncEnumerator(t));
    }
    private sealed class Envelope<R, S, K, SF, KF>(IServiceProvider services, R request, CancellationToken api,
        Func<S, R, CancellationToken, IAsyncEnumerator<int>> start) : IAsyncEnumerable<int>
        where S : class, IAsyncDisposable where K : class, IStreamItemMap<R, int>, IAsyncDisposable
        where SF : class, IOwnedSessionFactory<R, S> where KF : class, IOwnedSessionFactory<R, K>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) => new Cursor(services, request, api, token, start);
        private sealed class Cursor : IAsyncEnumerator<int>
        {
            private readonly IServiceProvider _services; private readonly R _request; private readonly CancellationToken _api, _enumeration;
            private readonly Func<S, R, CancellationToken, IAsyncEnumerator<int>> _start; private readonly IAsyncEnumerator<int> _body;
            private IAsyncEnumerator<int>? _source; private S? _receiver; private K? _hook; private CancellationTokenSource? _linked;
            private bool _started, _ended, _disposed;
            public Cursor(IServiceProvider provider, R value, CancellationToken apiToken, CancellationToken enumeration,
                Func<S, R, CancellationToken, IAsyncEnumerator<int>> factory)
            { _services = provider; _request = value; _api = apiToken; _enumeration = enumeration; _start = factory; _body = Items().GetAsyncEnumerator(); }
            public int Current => _body.Current;
            private async IAsyncEnumerable<int> Items()
            {
                var token = !_api.CanBeCanceled ? _enumeration : !_enumeration.CanBeCanceled || _api == _enumeration ? _api
                    : (_linked = CancellationTokenSource.CreateLinkedTokenSource(_api, _enumeration)).Token;
                token.ThrowIfCancellationRequested(); _hook = _services.GetRequiredService<KF>().Create(_request, token) ?? throw new InvalidOperationException();
                token.ThrowIfCancellationRequested(); _receiver = _services.GetRequiredService<SF>().Create(_request, token) ?? throw new InvalidOperationException();
                _source = _start(_receiver, _request, token); _started = true;
                while (await _source.MoveNextAsync().ConfigureAwait(false))
                { var item = _source.Current; token.ThrowIfCancellationRequested(); yield return _hook.Map(_request, item, token); }
            }
            public async ValueTask<bool> MoveNextAsync()
            {
                if (_ended) return false;
                try { var value = await _body.MoveNextAsync().ConfigureAwait(false); _ended = !value; return value; }
                catch { _ended = true; if (!_started) await Release().ConfigureAwait(false); throw; }
            }
            public async ValueTask DisposeAsync()
            {
                if (_disposed) return; _disposed = _ended = true;
                try { await _body.DisposeAsync().ConfigureAwait(false); }
                finally { await Release().ConfigureAwait(false); }
            }
            private async ValueTask Release()
            {
                var source = _source; var receiver = _receiver; var hook = _hook; var linked = _linked;
                _source = null; _receiver = null; _hook = null; _linked = null;
                try { if (source != null) await source.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    try { if (receiver != null) await receiver.DisposeAsync().ConfigureAwait(false); }
                    finally
                    {
                        try { if (hook != null) await hook.DisposeAsync().ConfigureAwait(false); }
                        finally { linked?.Dispose(); }
                    }
                }
            }
        }
    }
}
