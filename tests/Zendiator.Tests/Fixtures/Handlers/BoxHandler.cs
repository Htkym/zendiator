using global::Zendiator;

namespace Zendiator.Tests;

public sealed class BoxHandler<T> : ISyncRequestHandler<Box<T>, int>
    where T : allows ref struct
{
    public int Handle(scoped Box<T> request, CancellationToken cancellationToken) => typeof(T).Name.Length;
}
