namespace Zendiator.Benchmarks;

public sealed class MoB3<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker3, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B3++;
        return next(message, cancellationToken);
    }
}
