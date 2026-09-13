using global::Zendiator;

namespace Zendiator.Tests;

public sealed class Validation : IPipelineBehavior<Validate, Outcome>
{
    public ValueTask<Outcome> HandleAsync<TNext>(Validate request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<Validate, Outcome> => request.Valid
            ? next.InvokeAsync(request, cancellationToken) : new(new Outcome(false));
}
