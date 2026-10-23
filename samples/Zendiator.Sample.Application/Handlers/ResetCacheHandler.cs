using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

public sealed class ResetCacheHandler(ILogger<ResetCacheHandler> logger) : ISyncRequestHandler<ResetCache>
{
    public void Handle(ResetCache request, CancellationToken cancellationToken) =>
        logger.LogInformation("Reset cache");
}
