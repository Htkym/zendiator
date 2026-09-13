using global::Zendiator;

namespace Zendiator.Tests;

public readonly ref struct MultiRow<T> : ISyncMultiRequest<string>
    where T : allows ref struct
{
    public MultiRow(int id, T value)
    {
        Id = id;
        Value = value;
    }
    public readonly int Id;
    public readonly T Value;
}
