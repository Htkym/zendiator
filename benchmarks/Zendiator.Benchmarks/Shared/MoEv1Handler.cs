namespace Zendiator.Benchmarks;

public sealed class MoEv1Handler : global::Mediator.INotificationHandler<MoEv1>
{
    public ValueTask Handle(MoEv1 notification, CancellationToken cancellationToken)
    {
        MoEventCounters.H1++;
        return default;
    }
}
