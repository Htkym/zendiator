namespace Zendiator;

/// <summary>A generated, value-type continuation. Do not retain it after the dispatch finishes.</summary>
public interface IRequestContinuation<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    ValueTask<TResponse> InvokeAsync(TRequest request, CancellationToken cancellationToken);
}
/// <summary>A generated, value-type continuation for requests without a response.</summary>
public interface IRequestContinuation<TRequest>
    where TRequest : IRequest
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    ValueTask InvokeAsync(TRequest request, CancellationToken cancellationToken);
}
