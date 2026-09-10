using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Logging behavior for commands without a response.</summary>
public sealed class CommandLoggingBehavior<TRequest>(ILogger<CommandLoggingBehavior<TRequest>> logger)
    : IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public async ValueTask HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest>
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        try
        {
            await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        }
    }
}
