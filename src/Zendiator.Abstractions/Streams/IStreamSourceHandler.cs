namespace Zendiator;

/// <summary>Opens one typed, asynchronous cursor for a streaming request.</summary>
/// <remarks>
/// The generated Mediator returns an <see cref="IAsyncEnumerable{TItem}"/>. Each enumeration owns an
/// independent cursor, and handler resolution and this method are deferred until its first Move.
/// A cursor can be a class or a struct and must be accessible to the generated route.
/// Dependencies remain borrowed. The operation disposes the cursor and any sessions it creates.
/// </remarks>
public interface IStreamSourceHandler<TRequest, TItem, TCursor>
    where TRequest : IStreamRequest<TItem>
    where TCursor : IAsyncEnumerator<TItem>
{
    /// <summary>Returns one cursor operation consumed once by the generated route.</summary>
    ValueTask<TCursor> OpenAsync(TRequest request, CancellationToken cancellationToken);
}
