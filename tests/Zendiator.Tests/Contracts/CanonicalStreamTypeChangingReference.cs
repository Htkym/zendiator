using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Tests.Contracts;

// Independent compiler iterator: no IR/SG adapter; each item is held at its declared type.
internal static class CanonicalStreamTypeChangingReference
{
    public static IAsyncEnumerable<TOut> MapAsyncMap<R, TIn, TMid, TOut, TFirst, TLast>(IServiceProvider services, R request,
        Func<R, CancellationToken, IAsyncEnumerable<TIn>> start, CancellationToken api = default)
        where TFirst : IStreamItemMap<R, TIn, TMid> where TLast : IAsyncStreamItemMap<R, TMid, TOut>
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(start);
        return Run();
        async IAsyncEnumerable<TOut> Run([EnumeratorCancellation] CancellationToken enumeration = default)
        {
            CancellationTokenSource? linked = null; IAsyncEnumerator<TIn>? source = null;
            IStreamItemMap<R, TIn, TMid>? first = null; IAsyncStreamItemMap<R, TMid, TOut>? last = null;
            try
            {
                var token = !api.CanBeCanceled ? enumeration : !enumeration.CanBeCanceled || api == enumeration ? api
                    : (linked = CancellationTokenSource.CreateLinkedTokenSource(api, enumeration)).Token;
                token.ThrowIfCancellationRequested(); source = start(request, token).GetAsyncEnumerator(token);
                while (true)
                {
                    token.ThrowIfCancellationRequested(); if (!await source.MoveNextAsync().ConfigureAwait(false)) yield break;
                    var input = source.Current; token.ThrowIfCancellationRequested();
                    first ??= (IStreamItemMap<R, TIn, TMid>)services.GetRequiredService(typeof(TFirst));
                    var middle = first.Map(request, input, token); token.ThrowIfCancellationRequested();
                    last ??= (IAsyncStreamItemMap<R, TMid, TOut>)services.GetRequiredService(typeof(TLast));
                    yield return await last.MapAsync(request, middle, token).ConfigureAwait(false);
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
