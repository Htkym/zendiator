using global::Zendiator;

namespace Zendiator.Tests;

public sealed class ValidateHandler : ICommandHandler<Validate, Outcome>
{
    public ValueTask<Outcome> HandleAsync(Validate request, CancellationToken cancellationToken) => new(new Outcome(true));
}
