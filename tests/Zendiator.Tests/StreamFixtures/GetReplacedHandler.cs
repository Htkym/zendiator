using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetReplacedHandler : IStreamRequestHandler<GetReplaced, int>
{
    public static int Seen = -1;
    public async IAsyncEnumerable<int> HandleAsync(GetReplaced request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Seen = request.Count;
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
