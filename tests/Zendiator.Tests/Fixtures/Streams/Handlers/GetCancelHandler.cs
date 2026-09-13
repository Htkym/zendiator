using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetCancelHandler : IStreamRequestHandler<GetCancel, int>
{
    public static CancellationToken Seen;
    public async IAsyncEnumerable<int> HandleAsync(GetCancel request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Seen = cancellationToken;
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}
