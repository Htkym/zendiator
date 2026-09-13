using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

/// <summary>Purges a household. No <c>Unit</c> appears in user code.</summary>
public sealed class PurgeHouseholdHandler(ILogger<PurgeHouseholdHandler> logger) : IRequestHandler<PurgeHouseholdCommand>
{
    public ValueTask HandleAsync(PurgeHouseholdCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Purged {Id}", command.Id);
        return default;
    }
}
