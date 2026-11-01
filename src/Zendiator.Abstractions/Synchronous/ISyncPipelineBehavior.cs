namespace Zendiator;

public interface ISyncPipelineBehavior<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    /// <summary>Executes this behavior without retaining the request.</summary>
    TResponse Handle<TNext>(scoped TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest, TResponse>;
}
public interface ISyncPipelineBehavior<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    /// <summary>Executes this behavior without retaining the request.</summary>
    void Handle<TNext>(scoped TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest>;
}
