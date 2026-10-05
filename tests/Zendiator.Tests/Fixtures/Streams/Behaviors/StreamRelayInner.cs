using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamRelayInner(Trace trace) : IStreamPipelineBehavior<GetRelayMixed, int>
{
    public IAsyncEnumerable<int> HandleAsync<TNext>(GetRelayMixed request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetRelayMixed, int>
    {
        trace.Add("mn");
        return next.InvokeAsync(request, cancellationToken);
    }
}
