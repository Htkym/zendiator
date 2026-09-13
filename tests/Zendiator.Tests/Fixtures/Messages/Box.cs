using global::Zendiator;

namespace Zendiator.Tests;

public readonly ref struct Box<T> : ISyncRequest<int>
    where T : allows ref struct
{
    public Box(T value) => Value = value;
    public readonly T Value;
}
