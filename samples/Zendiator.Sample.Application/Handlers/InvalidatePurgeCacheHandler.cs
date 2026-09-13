using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Invalidates caches first when a household is purged.</summary>
[HandlerOrder(Order = -1)]
public sealed class InvalidatePurgeCacheHandler(ILogger<InvalidatePurgeCacheHandler> logger) : INotificationHandler<HouseholdPurged>
{
    public ValueTask HandleAsync(HouseholdPurged notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Invalidated purge cache {Id}", notification.Id);
        return default;
    }
}
