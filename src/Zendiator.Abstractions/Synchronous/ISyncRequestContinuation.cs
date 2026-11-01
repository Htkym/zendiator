namespace Zendiator;

/// <summary>A generated, value-type synchronous continuation. Must not retain the request.</summary>
public interface ISyncRequestContinuation<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    TResponse Invoke(scoped TRequest request, CancellationToken cancellationToken);
}
public interface ISyncRequestContinuation<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    void Invoke(scoped TRequest request, CancellationToken cancellationToken);
}
