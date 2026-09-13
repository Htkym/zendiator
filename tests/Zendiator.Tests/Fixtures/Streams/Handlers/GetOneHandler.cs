using global::Zendiator;

namespace Zendiator.Tests;

public sealed class GetOneHandler : IStreamRequestHandler<GetOne, int>
{
    public async IAsyncEnumerable<int> HandleAsync(GetOne request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return request.Value;
    }
}
