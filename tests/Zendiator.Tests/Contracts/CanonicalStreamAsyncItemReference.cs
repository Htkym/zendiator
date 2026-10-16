using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// Independent compiler async iterator; no generator, IR or cursor adapter.
internal static class CanonicalStreamAsyncItemReference
{
    public static IAsyncEnumerable<TItem> FilterMapFilter<TRequest, TItem, TFirst, TMap, TLast>(
        IServiceProvider services, TRequest request, Func<TRequest, CancellationToken, IAsyncEnumerable<TItem>> start,
        CancellationToken api)
        where TFirst : IAsyncStreamItemFilter<TRequest, TItem>
        where TMap : IAsyncStreamItemMap<TRequest, TItem>
        where TLast : IStreamItemFilter<TRequest, TItem>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(start);
        return Run();

        async IAsyncEnumerable<TItem> Run([EnumeratorCancellation] CancellationToken enumeration = default)
        {
            CancellationTokenSource? linked = null;
            IAsyncEnumerator<TItem>? source = null;
            TFirst? first = default; TMap? map = default; TLast? last = default;
            var firstResolved = false; var mapResolved = false; var lastResolved = false;
            try
            {
                var token = !api.CanBeCanceled ? enumeration : !enumeration.CanBeCanceled || api == enumeration ? api
                    : (linked = CancellationTokenSource.CreateLinkedTokenSource(api, enumeration)).Token;
                token.ThrowIfCancellationRequested();
                source = start(request, token).GetAsyncEnumerator(token);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (!await source.MoveNextAsync().ConfigureAwait(false)) yield break;
                    var item = source.Current;
                    token.ThrowIfCancellationRequested();
                    if (!firstResolved) { first = (TFirst)services.GetRequiredService(typeof(TFirst)); firstResolved = true; }
                    if (!await first!.AcceptAsync(request, item, token).ConfigureAwait(false)) continue;
                    token.ThrowIfCancellationRequested();
                    if (!mapResolved) { map = (TMap)services.GetRequiredService(typeof(TMap)); mapResolved = true; }
                    item = await map!.MapAsync(request, item, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!lastResolved) { last = (TLast)services.GetRequiredService(typeof(TLast)); lastResolved = true; }
                    if (!last!.Accept(request, item, token)) continue;
                    yield return item;
                }
            }
            finally
            {
                var owned = source; source = null;
                try { if (owned is not null) await owned.DisposeAsync().ConfigureAwait(false); }
                finally { linked?.Dispose(); }
            }
        }
    }
}
