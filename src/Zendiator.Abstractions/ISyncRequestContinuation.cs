namespace Zendiator;

/// <summary>A generated, value-type synchronous continuation. Must not retain the request.</summary>
public interface ISyncRequestContinuation<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    TResponse Invoke(scoped TRequest request, CancellationToken cancellationToken);
}
/// <summary>A generated, value-type synchronous continuation for requests without a response.</summary>
public interface ISyncRequestContinuation<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    void Invoke(scoped TRequest request, CancellationToken cancellationToken);
}
