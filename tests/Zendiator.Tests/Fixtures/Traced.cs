using global::Zendiator;

namespace Zendiator.Tests;

public abstract class Traced<TRequest, TResponse>(Trace trace, string name) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        trace.Add(name);
        try { return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false); }
        finally { trace.Add("/" + name); }
    }
}
