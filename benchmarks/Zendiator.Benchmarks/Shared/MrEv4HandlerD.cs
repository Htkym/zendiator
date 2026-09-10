namespace Zendiator.Benchmarks;

public sealed class MrEv4HandlerD : global::MediatR.INotificationHandler<MrEv4>
{
    public Task Handle(MrEv4 notification, CancellationToken cancellationToken)
    {
        MrEventCounters.H4++;
        return Task.CompletedTask;
    }
}
