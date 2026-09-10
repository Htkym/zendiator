using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

/// <summary>Single open behavior applied to every request in this compilation.</summary>
public sealed class TracingBehavior<TRequest, TResponse>(Trace trace) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        trace.Events.Add("before");
        try
        {
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            trace.Events.Add("after");
        }
    }
}
