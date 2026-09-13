using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Logging behavior for streams. Runs once per enumeration, not per item.</summary>
public sealed class StreamLoggingBehavior<TRequest, TItem>(ILogger<StreamLoggingBehavior<TRequest, TItem>> logger)
    : IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public async IAsyncEnumerable<TItem> HandleAsync<TNext>(TRequest request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<TRequest, TItem>
    {
        logger.LogInformation("Streaming {Request}", typeof(TRequest).Name);
        try
        {
            await foreach (var item in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            logger.LogInformation("Streamed {Request}", typeof(TRequest).Name);
        }
    }
}
