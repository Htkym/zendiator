using Microsoft.Extensions.Logging;
using Zendiator.Sample.Contracts;

namespace Zendiator.Sample.Application;

public sealed class LegacyPingHandler : ICommandHandler<LegacyPing>
{
    public ValueTask<Unit> HandleAsync(LegacyPing request, CancellationToken cancellationToken) => new(Unit.Value);
}
