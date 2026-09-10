using global::Zendiator;

namespace Zendiator.Tests;

public sealed class CompleteHandler : ICommandHandler<Complete>
{
    public ValueTask<Unit> HandleAsync(Complete request, CancellationToken cancellationToken) => new(Unit.Value);
}
