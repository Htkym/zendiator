using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamA3(Trace trace) : IStreamPipelineBehavior<GetPiped3, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(GetPiped3 request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<GetPiped3, int>
    {
        trace.Add("a3");
        try { await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken)) yield return i; }
        finally { trace.Add("/a3"); }
    }
}
