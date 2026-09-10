using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

public sealed class AsmStreamTrace(Trace trace) : IStreamPipelineBehavior<AsmNumbers, int>
{
    public async IAsyncEnumerable<int> HandleAsync<TNext>(AsmNumbers request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<AsmNumbers, int>
    {
        trace.Events.Add("s-before");
        try
        {
            await foreach (var i in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
                yield return i;
        }
        finally { trace.Events.Add("s-after"); }
    }
}
