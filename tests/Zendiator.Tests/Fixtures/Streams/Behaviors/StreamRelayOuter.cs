using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamRelayOuter(Trace trace) : IStreamPipelineBehavior<GetRelayMixed, int>
{
    public IAsyncEnumerable<int> HandleAsync<TNext>(GetRelayMixed request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetRelayMixed, int>
    {
        trace.Add("mo");
        return next.InvokeAsync(request, cancellationToken);
    }
}
