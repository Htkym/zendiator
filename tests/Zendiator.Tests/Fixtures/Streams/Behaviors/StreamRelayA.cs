using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamRelayA(Trace trace) : IStreamPipelineBehavior<GetRelay, int>
{
    public IAsyncEnumerable<int> HandleAsync<TNext>(GetRelay request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetRelay, int>
    {
        trace.Add("ra");
        return next.InvokeAsync(request, cancellationToken);
    }
}
