using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetPiped5Handler : IStreamRequestHandler<GetPiped5, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetPiped5 request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}
