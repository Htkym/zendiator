namespace Zendiator.Benchmarks;

public sealed class MrEv16H14 : global::MediatR.INotificationHandler<MrEv16>
{
    public Task Handle(MrEv16 n, CancellationToken c) { MrEventCounters.H16++; return Task.CompletedTask; }
}
