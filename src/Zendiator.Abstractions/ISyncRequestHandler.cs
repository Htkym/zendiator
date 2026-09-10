namespace Zendiator;

/// <summary>Handles one synchronous request. The request is passed by scoped argument and must not escape.</summary>
public interface ISyncRequestHandler<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    /// <summary>Handles the request synchronously without retaining it.</summary>
    TResponse Handle(scoped TRequest request, CancellationToken cancellationToken);
}
/// <summary>Handles one synchronous request without a response.</summary>
public interface ISyncRequestHandler<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    /// <summary>Handles the request synchronously without retaining it.</summary>
    void Handle(scoped TRequest request, CancellationToken cancellationToken);
}
