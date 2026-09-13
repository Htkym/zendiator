namespace Zendiator.Benchmarks;

public sealed class MoB4<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker4, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B4++;
        return next(message, cancellationToken);
    }
}
