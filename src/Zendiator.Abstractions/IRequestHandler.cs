namespace Zendiator;

/// <summary>Handles one request. Cancellation and business errors belong to the handler.</summary>
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request. Await the returned value only once.</summary>
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
/// <summary>Handles one request without a meaningful response (native void route).</summary>
public interface IRequestHandler<TRequest>
    where TRequest : IRequest
{
    /// <summary>Handles the request. Await the returned value only once.</summary>
    ValueTask HandleAsync(TRequest request, CancellationToken cancellationToken);
}
