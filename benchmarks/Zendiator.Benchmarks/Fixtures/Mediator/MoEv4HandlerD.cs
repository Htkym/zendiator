namespace Zendiator.Benchmarks;

public sealed class MoEv4HandlerD : global::Mediator.INotificationHandler<MoEv4>
{
    public ValueTask Handle(MoEv4 notification, CancellationToken cancellationToken)
    {
        MoEventCounters.H4++;
        return default;
    }
}
