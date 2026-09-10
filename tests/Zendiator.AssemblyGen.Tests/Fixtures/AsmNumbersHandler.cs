using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

public sealed class AsmNumbersHandler : IStreamRequestHandler<AsmNumbers, int>
{
    public async IAsyncEnumerable<int> HandleAsync(AsmNumbers request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
