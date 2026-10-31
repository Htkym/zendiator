using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Logging at an explicit opaque cursor boundary, once per enumeration.</summary>
public sealed class StreamLoggingBehavior<TRequest, TItem>(ILogger<StreamLoggingBehavior<TRequest, TItem>> logger)
    : IStreamMiddleware<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public async ValueTask<IAsyncEnumerator<TItem>> OpenAsync(TRequest request,
        IStreamUpstream<TRequest, TItem> next, CancellationToken cancellationToken)
    {
        logger.LogInformation("Streaming {Request}", typeof(TRequest).Name);
        try
        {
            var cursor = await next.OpenAsync(request, cancellationToken).ConfigureAwait(false);
            return new LoggingCursor(cursor, logger);
        }
        catch
        {
            logger.LogInformation("Streamed {Request}", typeof(TRequest).Name);
            throw;
        }
    }

    private sealed class LoggingCursor(IAsyncEnumerator<TItem> cursor,
        ILogger<StreamLoggingBehavior<TRequest, TItem>> logger) : IAsyncEnumerator<TItem>
    {
        private bool _disposed;
        public TItem Current => cursor.Current;
        public ValueTask<bool> MoveNextAsync() => cursor.MoveNextAsync();
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                await cursor.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                logger.LogInformation("Streamed {Request}", typeof(TRequest).Name);
            }
        }
    }
}
