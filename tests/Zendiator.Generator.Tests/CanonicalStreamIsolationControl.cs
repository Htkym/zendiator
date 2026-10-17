using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Generator.Tests;

// Migration experiment only: per-layer async-iterator isolation, not the 0.4.0 runtime.
// One borrowed-class, reference-request, same-int-item F1 -> Map -> F2 shape; no wrapper/session.
internal static class CanonicalStreamIsolationControl
{
    public static IAsyncEnumerable<int> Create<TRequest, TFirst, TMap, TLast>(
        IServiceProvider services, TRequest request, Func<TRequest, CancellationToken, IAsyncEnumerable<int>> start,
        Action<string>? observeGuard = null)
        where TFirst : IStreamItemFilter<TRequest, int>
        where TMap : IStreamItemMap<TRequest, int>
        where TLast : IStreamItemFilter<TRequest, int>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(start);
        return Last(Map(First(Source())));

        void Guard(string stage, CancellationToken token)
        {
            observeGuard?.Invoke("guard:" + stage);
            ArgumentNullException.ThrowIfNull(request);
            token.ThrowIfCancellationRequested();
        }

        async IAsyncEnumerable<int> Source([EnumeratorCancellation] CancellationToken token = default)
        {
            Guard("H.start", token);
            // start includes the handler DI factory; it is inside this layer's boundary.
            var inner = start(request, token).GetAsyncEnumerator(token);
            try
            {
                while (true)
                {
                    Guard("H.move", token);
                    if (!await inner.MoveNextAsync().ConfigureAwait(false)) yield break;
                    yield return inner.Current;
                }
            }
            finally
            {
                // Observe cleanup entry without a cancellation guard that could skip disposal.
                observeGuard?.Invoke("cleanup:H");
                await inner.DisposeAsync().ConfigureAwait(false);
            }
        }

        async IAsyncEnumerable<int> First(IAsyncEnumerable<int> source,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            IStreamItemFilter<TRequest, int>? service = null;
            await foreach (var item in source.WithCancellation(token).ConfigureAwait(false))
            {
                Guard("F1", token);
                service ??= (IStreamItemFilter<TRequest, int>)services.GetRequiredService(typeof(TFirst));
                if (service.Accept(request, item, token)) yield return item;
            }
        }

        async IAsyncEnumerable<int> Map(IAsyncEnumerable<int> source,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            IStreamItemMap<TRequest, int>? service = null;
            await foreach (var item in source.WithCancellation(token).ConfigureAwait(false))
            {
                Guard("Map", token);
                service ??= (IStreamItemMap<TRequest, int>)services.GetRequiredService(typeof(TMap));
                yield return service.Map(request, item, token);
            }
        }

        async IAsyncEnumerable<int> Last(IAsyncEnumerable<int> source,
            [EnumeratorCancellation] CancellationToken token = default)
        {
            IStreamItemFilter<TRequest, int>? service = null;
            await foreach (var item in source.WithCancellation(token).ConfigureAwait(false))
            {
                Guard("F2", token);
                service ??= (IStreamItemFilter<TRequest, int>)services.GetRequiredService(typeof(TLast));
                if (service.Accept(request, item, token)) yield return item;
            }
        }
    }
}
