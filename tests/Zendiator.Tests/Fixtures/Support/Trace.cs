using global::Zendiator;

namespace Zendiator.Tests;

public sealed class Trace
{
    public bool Enabled { get; set; } = true;
    public List<string> Events { get; } = [];
    public void Add(string value) { if (Enabled) Events.Add(value); }
}
