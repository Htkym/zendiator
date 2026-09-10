using global::Zendiator;

namespace Zendiator.AssemblyGen.Tests;

/// <summary>Tracing state for behavior verification.</summary>
public sealed class Trace
{
    public List<string> Events { get; } = [];
}
