namespace Zendiator;

/// <summary>Produces an asynchronous enumerable for a streaming request.</summary>
/// <remarks>
/// Each enumeration calls this method once and requires an independent input cursor.
/// For flat public StreamAsync routes with no stages and two noncancelable tokens,
/// handler and cursor acquisition occurs at GetAsyncEnumerator; the producer/caller owns cleanup.
/// Other routes and StreamSource.ConsumeAsync retain first-Move acquisition and guarded cleanup.
/// DI retains ownership of its cached receiver. Keep its scope alive through enumeration and disposal.
/// </remarks>
public interface IStreamHandler<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>Returns an enumerable whose cursor is acquired once for this enumeration.</summary>
    IAsyncEnumerable<TItem> Handle(TRequest request, CancellationToken cancellationToken);
}
