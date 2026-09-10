using Zendiator;

namespace Zendiator.Allocation.Tests;

public sealed class AllocStreamHandler : IStreamRequestHandler<AllocStream, int>
{
    public async IAsyncEnumerable<int> HandleAsync(AllocStream request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
        await Task.CompletedTask;
    }
}
