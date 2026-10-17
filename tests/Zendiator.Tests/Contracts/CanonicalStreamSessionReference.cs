using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// Independent ordinary iterator for item execution; its enclosing cursor owns sessions until Dispose.
internal static class CanonicalStreamSessionReference
{
    public static IAsyncEnumerable<int> Owned<R, H, K1, B, K2, FH, F1, F2>(IServiceProvider services, R request)
        where H : class, IStreamRequestHandler<R, int>, IAsyncDisposable where R : IStreamRequest<int>
        where K1 : class, IStreamItemMap<R, int>, IAsyncDisposable where K2 : class, IStreamItemMap<R, int>, IAsyncDisposable
        where B : class, IStreamItemMap<R, int> where FH : class, IOwnedSessionFactory<R, H>
        where F1 : class, IAsyncOwnedSessionFactory<R, K1> where F2 : class, IOwnedSessionFactory<R, K2> =>
        new Envelope<R, H, K1, B, K2, FH, F1, F2>(services, request);
    private sealed class Envelope<R, H, K1, B, K2, FH, F1, F2>(IServiceProvider services, R request) : IAsyncEnumerable<int>
        where H : class, IStreamRequestHandler<R, int>, IAsyncDisposable where R : IStreamRequest<int>
        where K1 : class, IStreamItemMap<R, int>, IAsyncDisposable where K2 : class, IStreamItemMap<R, int>, IAsyncDisposable
        where B : class, IStreamItemMap<R, int> where FH : class, IOwnedSessionFactory<R, H>
        where F1 : class, IAsyncOwnedSessionFactory<R, K1> where F2 : class, IOwnedSessionFactory<R, K2>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) => new Cursor(services, request, token);
        private sealed class Cursor : IAsyncEnumerator<int>
        {
            private H? _handler; private K1? _first; private K2? _last; private IAsyncEnumerator<int>? _source;
            private readonly IAsyncEnumerator<int> _body; private bool _started, _ended, _disposed;
            private readonly IServiceProvider _services; private readonly R _request; private readonly CancellationToken _token;
            public Cursor(IServiceProvider provider, R value, CancellationToken token)
            { _services = provider; _request = value; _token = token; _body = Items().GetAsyncEnumerator(); }
            public int Current => _body.Current;
            private async IAsyncEnumerable<int> Items()
            {
                _last = _services.GetRequiredService<F2>().Create(_request, _token) ?? throw new InvalidOperationException("Null session.");
                _first = await _services.GetRequiredService<F1>().CreateAsync(_request, _token).ConfigureAwait(false) ?? throw new InvalidOperationException("Null session.");
                _handler = _services.GetRequiredService<FH>().Create(_request, _token) ?? throw new InvalidOperationException("Null session.");
                _source = _handler.HandleAsync(_request, _token).GetAsyncEnumerator(_token); _started = true; B? middle = null;
                while (await _source.MoveNextAsync().ConfigureAwait(false))
                {
                    var item = _first.Map(_request, _source.Current, _token);
                    middle ??= _services.GetRequiredService<B>(); item = middle.Map(_request, item, _token);
                    yield return _last.Map(_request, item, _token);
                }
            }
            public async ValueTask<bool> MoveNextAsync()
            {
                if (_ended) return false;
                try { var result = await _body.MoveNextAsync().ConfigureAwait(false); _ended = !result; return result; }
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
                var source = _source; var handler = _handler; var first = _first; var last = _last;
                _source = null; _handler = null; _first = null; _last = null;
                try { if (source != null) await source.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    try { if (handler != null) await handler.DisposeAsync().ConfigureAwait(false); }
                    finally
                    {
                        try { if (first != null) await first.DisposeAsync().ConfigureAwait(false); }
                        finally { if (last != null) await last.DisposeAsync().ConfigureAwait(false); }
                    }
                }
            }
        }
    }
    public static IAsyncEnumerable<int> Stateless<R, H, K1, B, K2>(IServiceProvider services, R request)
        where R : IStreamRequest<int> where H : class, IStreamRequestHandler<R, int>
        where K1 : class, IStreamItemMap<R, int> where B : class, IStreamItemMap<R, int> where K2 : class, IStreamItemMap<R, int>
    {
        return Run();
        async IAsyncEnumerable<int> Run()
        {
            var source = services.GetRequiredService<H>().HandleAsync(request, default).GetAsyncEnumerator();
            K1? first = null; B? middle = null; K2? last = null;
            try
            {
                while (await source.MoveNextAsync().ConfigureAwait(false))
                {
                    var item = source.Current; first ??= services.GetRequiredService<K1>(); item = first.Map(request, item, default);
                    middle ??= services.GetRequiredService<B>(); item = middle.Map(request, item, default);
                    last ??= services.GetRequiredService<K2>(); yield return last.Map(request, item, default);
                }
            }
            finally { await source.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
