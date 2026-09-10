using global::Zendiator;

namespace Zendiator.Tests;

public readonly ref struct ParseRequest : ISyncRequest<int>
{
    public ParseRequest(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}
