namespace Zendiator;

/// <summary>Handles one streaming request. The returned enumerable is lazy; do not retain the request after enumeration finishes.</summary>
public interface IStreamRequestHandler<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>Handles the streaming request. Enumeration starts only when the returned enumerable is enumerated.</summary>
    IAsyncEnumerable<TItem> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
