using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetFilterHandler : IStreamRequestHandler<GetFilter, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetFilter request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
