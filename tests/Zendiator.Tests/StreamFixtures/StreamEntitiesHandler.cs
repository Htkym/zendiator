using global::Zendiator;

namespace Zendiator.Tests;

public sealed class StreamEntitiesHandler<T> : IStreamRequestHandler<StreamEntities<T>, T>
    where T : new()
{
    public async IAsyncEnumerable<T> HandleAsync(StreamEntities<T> request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return new T();
        }
    }
}
