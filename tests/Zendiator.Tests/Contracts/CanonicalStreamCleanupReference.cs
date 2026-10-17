using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// Independent ordinary iterator body. Only owned hooks are released by its enclosing cursor.
internal static class CanonicalStreamCleanupReference
{
    public static IAsyncEnumerable<int> Create<R, H, I, O, HF, IF, OF>(IServiceProvider services, R request)
        where R : IStreamRequest<int> where H : class, IStreamRequestHandler<R, int>
        where I : class, IStreamItemMap<R, int>, IAsyncDisposable where O : class, IStreamItemMap<R, int>, IAsyncDisposable
        where HF : class, IBorrowedSessionFactory<R, H> where IF : class, IOwnedSessionFactory<R, I> where OF : class, IOwnedSessionFactory<R, O> =>
        new Envelope<R, H, I, O, HF, IF, OF>(services, request);
    private sealed class Envelope<R, H, I, O, HF, IF, OF>(IServiceProvider services, R request) : IAsyncEnumerable<int>
        where R : IStreamRequest<int> where H : class, IStreamRequestHandler<R, int>
        where I : class, IStreamItemMap<R, int>, IAsyncDisposable where O : class, IStreamItemMap<R, int>, IAsyncDisposable
        where HF : class, IBorrowedSessionFactory<R, H> where IF : class, IOwnedSessionFactory<R, I> where OF : class, IOwnedSessionFactory<R, O>
    {
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) => new Cursor(services, request, token);
        private sealed class Cursor : IAsyncEnumerator<int>
        {
            private readonly IServiceProvider _services; private readonly R _request; private readonly CancellationToken _token;
            private readonly IAsyncEnumerator<int> _body; private IAsyncEnumerator<int>? _source; private I? _inner; private O? _outer; private H? _borrowed;
            private bool _started, _ended, _disposed;
            public Cursor(IServiceProvider provider, R request, CancellationToken token)
            { _services = provider; _request = request; _token = token; _body = Items().GetAsyncEnumerator(); }
            public int Current => _body.Current;
            private async IAsyncEnumerable<int> Items()
            {
                _outer = _services.GetRequiredService<OF>().Create(_request, _token) ?? throw new InvalidOperationException();
                _inner = _services.GetRequiredService<IF>().Create(_request, _token) ?? throw new InvalidOperationException();
                _borrowed = _services.GetRequiredService<HF>().Create(_request, _token) ?? throw new InvalidOperationException();
                _source = _borrowed.HandleAsync(_request, _token).GetAsyncEnumerator(_token); _started = true;
                while (await _source.MoveNextAsync().ConfigureAwait(false))
                    yield return _outer.Map(_request, _inner.Map(_request, _source.Current, _token), _token);
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
                var source = _source; var inner = _inner; var outer = _outer; _source = null; _inner = null; _outer = null; _borrowed = null;
                try { if (source != null) await source.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    try { if (inner != null) await inner.DisposeAsync().ConfigureAwait(false); }
                    finally { if (outer != null) await outer.DisposeAsync().ConfigureAwait(false); }
                }
            }
        }
    }
}
