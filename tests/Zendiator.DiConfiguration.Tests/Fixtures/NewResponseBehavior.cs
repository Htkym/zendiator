using Di.Generated;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.DiConfiguration.Tests;

public sealed class NewResponseBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse> where TResponse : new()
{
    public static int Calls;
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        Calls++;
        return next.InvokeAsync(request, cancellationToken);
    }
}
