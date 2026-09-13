using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamTraceFail(Trace trace) : IStreamPipelineBehavior<GetFail, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetFail request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetFail, int>
    {
        trace.Add("fail-before");
        try
        {
            await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
                yield return i;
            trace.Add("fail-after");
        }
        finally { trace.Add("/fail"); }
    }
}
