using global::Zendiator;

namespace Zendiator.Tests;

public sealed record TraceLog
{
    public List<string> Entries { get; } = [];
}
