namespace Zendiator;

/// <summary>Handles one synchronous request. The request is passed by scoped argument and must not escape.</summary>
public interface ISyncRequestHandler<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    TResponse Handle(scoped TRequest request, CancellationToken cancellationToken);
}
public interface ISyncRequestHandler<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    void Handle(scoped TRequest request, CancellationToken cancellationToken);
}
