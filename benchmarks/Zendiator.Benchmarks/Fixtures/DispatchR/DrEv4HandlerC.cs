namespace Zendiator.Benchmarks;

public sealed class DrEv4HandlerC : global::DispatchR.Abstractions.Notification.INotificationHandler<DrEv4>
{
    public ValueTask Handle(DrEv4 notification, CancellationToken cancellationToken)
    {
        DrEventCounters.H4++;
        return ValueTask.CompletedTask;
    }
}
