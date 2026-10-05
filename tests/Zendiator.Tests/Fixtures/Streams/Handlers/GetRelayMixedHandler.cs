using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetRelayMixedHandler(Trace trace) : IStreamRequestHandler<GetRelayMixed, int>
{
    public static readonly InvalidOperationException Failure = new("relay handler");

    public async IAsyncEnumerable<int> HandleAsync(GetRelayMixed request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        trace.Add("mh");
        try
        {
            for (var i = 0; i < request.Count; i++)
            {
                await Task.Yield();
                if (i == request.FailAt) throw Failure;
                yield return i;
            }
        }
        finally { trace.Add("/mh"); }
    }
}
