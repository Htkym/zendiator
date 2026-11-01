using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

public sealed class PurgeHouseholdHandler(ILogger<PurgeHouseholdHandler> logger) : IRequestHandler<PurgeHouseholdCommand>
{
    public ValueTask HandleAsync(PurgeHouseholdCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Purged {Id}", command.Id);
        return default;
    }
}
