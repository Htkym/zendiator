namespace Zendiator.Benchmarks;

public sealed class MrEv1Handler : global::MediatR.INotificationHandler<MrEv1>
{
    public Task Handle(MrEv1 notification, CancellationToken cancellationToken)
    {
        MrEventCounters.H1++;
        return Task.CompletedTask;
    }
}
