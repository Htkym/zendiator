using global::Zendiator;

namespace Zendiator.Tests;

public sealed class CommandTracer<TRequest>(Trace trace) : IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public async ValueTask HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest>
    {
        trace.Add("cmd");
        try
        {
            await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            trace.Add("/cmd");
        }
    }
}
