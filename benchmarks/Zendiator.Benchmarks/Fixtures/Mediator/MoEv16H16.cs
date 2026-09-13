namespace Zendiator.Benchmarks;

public sealed class MoEv16H16 : global::Mediator.INotificationHandler<MoEv16>
{
    public ValueTask Handle(MoEv16 n, CancellationToken c) { MoEventCounters.H16++; return default; }
}
