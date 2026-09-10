namespace Zendiator.Benchmarks;

public sealed class FoPingHandler
{
    public int Handle(FoPing message) => message.Value + 1;
}
