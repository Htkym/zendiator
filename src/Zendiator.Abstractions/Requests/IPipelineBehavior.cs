namespace Zendiator;

/// <summary>Wraps a request. A continuation may be invoked repeatedly, sequentially, or not at all.</summary>
public interface IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Executes this behavior without boxing or allocating a continuation delegate.</summary>
    ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>;
}
/// <summary>Wraps a request without a response.</summary>
public interface IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    /// <summary>Executes this behavior without boxing or allocating a continuation delegate.</summary>
    ValueTask HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest>;
}
