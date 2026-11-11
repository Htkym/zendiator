namespace Zendiator;

/// <summary>Opens an opaque asynchronous cursor around the remaining Stream pipeline.</summary>
/// <remarks>
/// The explicit <see cref="IAsyncEnumerator{TItem}"/> boundary permits arbitrary middleware.
/// Only one upstream cursor may be active at a time. Middleware may open the upstream again
/// sequentially after normal completion and cleanup. Early disposal or failure aborts that upstream
/// lease and does not permit reopening. It must dispose each cursor it acquires.
/// An inner end of stream does not stop the outer operation. The framework never retries
/// automatically. Repeating a normally completed upstream or choosing another cursor can deliver
/// duplicate items; retrying a failed upstream is not permitted. Do not retain next after this
/// operation finishes, overlap cursor operations, or abandon pending operations.
/// Dependencies and the upstream remain borrowed.
/// The item type is the middleware's pipeline input, which can differ from the request's source item
/// after a type-changing map. The generated route validates that connection.
/// </remarks>
public interface IStreamMiddleware<TRequest, TItem>
{
    /// <summary>Opens one cursor using a typed, borrowed upstream port.</summary>
    /// <remarks>
    /// Generated routes directly call an accessible public nonvirtual implementation declared on
    /// a sealed class, without boxing the port. Keep next generic when retaining it in a cursor.
    /// The default adapts implementations of the legacy overload.
    /// </remarks>
    ValueTask<IAsyncEnumerator<TItem>> OpenAsync<TNext>(TRequest request, TNext next,
        CancellationToken cancellationToken) where TNext : struct, IStreamUpstream<TRequest, TItem>
    {
        if (typeof(TNext) == typeof(LegacyUpstream))
            throw new NotSupportedException("A Stream middleware must implement an OpenAsync overload.");
        return OpenAsync(request, (IStreamUpstream<TRequest, TItem>)next, cancellationToken);
    }

    /// <summary>Adapts an interface upstream for legacy callers and implementations.</summary>
    /// <remarks>
    /// Existing implementations may retain this overload. The default calls a typed-only
    /// implementation. An interface upstream cannot recover the allocation avoided by a typed port.
    /// </remarks>
    ValueTask<IAsyncEnumerator<TItem>> OpenAsync(TRequest request, IStreamUpstream<TRequest, TItem> next,
        CancellationToken cancellationToken) => OpenAsync(request, new LegacyUpstream(next), cancellationToken);

    private readonly struct LegacyUpstream(IStreamUpstream<TRequest, TItem> next)
        : IStreamUpstream<TRequest, TItem>
    {
        public ValueTask<IAsyncEnumerator<TItem>> OpenAsync(TRequest request, CancellationToken cancellationToken) =>
            next.OpenAsync(request, cancellationToken);
    }
}
