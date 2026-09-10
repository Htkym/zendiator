using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetPiped3Handler : IStreamRequestHandler<GetPiped3, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetPiped3 request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}
