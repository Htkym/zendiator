using global::Zendiator;

namespace Zendiator.Tests;

public abstract class StreamTraced(Trace trace, string name)
{
    protected void Before() => trace.Add(name);
    protected void After() => trace.Add("/" + name);
}
