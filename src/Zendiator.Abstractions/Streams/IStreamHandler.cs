namespace Zendiator;

/// <summary>Produces an asynchronous enumerable for a streaming request.</summary>
/// <remarks>
/// Handler resolution, this method and GetAsyncEnumerator are deferred until the first Move.
/// Each enumeration owns an independent cursor obtained with its effective cancellation token.
/// DI retains ownership of the handler; the operation disposes the acquired cursor.
/// </remarks>
public interface IStreamHandler<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>Returns an enumerable whose cursor is acquired once for this enumeration.</summary>
    IAsyncEnumerable<TItem> Handle(TRequest request, CancellationToken cancellationToken);
}
