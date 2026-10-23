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
    /// <summary>Returns one cursor operation consumed once by the generated route.</summary>
    ValueTask<IAsyncEnumerator<TItem>> OpenAsync(TRequest request, IStreamUpstream<TRequest, TItem> next,
        CancellationToken cancellationToken);
}
