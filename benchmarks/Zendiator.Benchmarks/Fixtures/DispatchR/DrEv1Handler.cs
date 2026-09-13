namespace Zendiator.Benchmarks;

public sealed class DrEv1Handler : global::DispatchR.Abstractions.Notification.INotificationHandler<DrEv1>
{
    public ValueTask Handle(DrEv1 notification, CancellationToken cancellationToken)
    {
        DrEventCounters.H1++;
        return ValueTask.CompletedTask;
    }
}
