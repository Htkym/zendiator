using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetRelayHandler(Trace trace) : IStreamRequestHandler<GetRelay, int>
{
    public static CancellationToken Seen;

    public async IAsyncEnumerable<int> HandleAsync(GetRelay request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Seen = cancellationToken;
        trace.Add("rh");
        try
        {
            for (var i = 0; i < request.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return i;
            }
        }
        finally { trace.Add("/rh"); }
    }
}
