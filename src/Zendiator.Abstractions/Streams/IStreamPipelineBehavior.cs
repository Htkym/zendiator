namespace Zendiator;

/// <summary>Wraps a streaming request. A continuation should be invoked at most once per enumeration.</summary>
public interface IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    IAsyncEnumerable<TItem> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<TRequest, TItem>;
}
