using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Audits purges after cache invalidation (see <see cref="InvalidatePurgeCacheHandler"/>).</summary>
public sealed class AuditPurgeHandler(ILogger<AuditPurgeHandler> logger) : INotificationHandler<HouseholdPurged>
{
    public ValueTask HandleAsync(HouseholdPurged notification, CancellationToken cancellationToken)
    {
        logger.LogInformation("Audited purge {Id}", notification.Id);
        return default;
    }
}
