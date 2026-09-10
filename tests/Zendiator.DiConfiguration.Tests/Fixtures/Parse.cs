using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public readonly ref struct Parse : ISyncRequest<int>
{
    public Parse(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}
