using Zendiator;

namespace Zendiator.FeatureBench;

public readonly ref struct ZSpan : ISyncRequest<int>
{
    public ZSpan(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}
