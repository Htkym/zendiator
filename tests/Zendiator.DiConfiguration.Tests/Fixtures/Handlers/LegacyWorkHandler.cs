using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class LegacyWorkHandler : ICommandHandler<LegacyWork>
{
    public ValueTask<Unit> HandleAsync(LegacyWork request, CancellationToken cancellationToken) => new(Unit.Value);
}
