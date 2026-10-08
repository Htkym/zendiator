using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamRelayB(Trace trace) : IStreamPipelineBehavior<GetRelay, int>
{
    public static readonly InvalidOperationException Failure = new("relay pre-processing");

    public IAsyncEnumerable<int> HandleAsync<TNext>(GetRelay request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetRelay, int>
    {
        trace.Add("rb");
        if (request.FailBefore) throw Failure;
        return next.InvokeAsync(request, cancellationToken);
    }
}
