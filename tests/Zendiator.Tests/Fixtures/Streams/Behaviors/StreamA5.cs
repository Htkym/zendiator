using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamA5(Trace trace) : IStreamPipelineBehavior<GetPiped5, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped5 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped5, int>
    {
        trace.Add("a5");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/a5"); }
    }
}
