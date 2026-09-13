namespace Zendiator;

/// <summary>A generated, value-type stream continuation. Do not retain it after the enumeration finishes.</summary>
public interface IStreamContinuation<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    IAsyncEnumerable<TItem> InvokeAsync(TRequest request, CancellationToken cancellationToken);
}
