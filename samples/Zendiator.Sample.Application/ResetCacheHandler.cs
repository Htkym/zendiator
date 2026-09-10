using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Resets cached state without a response.</summary>
public sealed class ResetCacheHandler(ILogger<ResetCacheHandler> logger) : ISyncRequestHandler<ResetCache>
{
    public void Handle(ResetCache request, CancellationToken cancellationToken) =>
        logger.LogInformation("Reset cache");
}
