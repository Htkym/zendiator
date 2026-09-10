namespace Zendiator.Benchmarks;

public sealed class MoB2<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker2, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B2++;
        return next(message, cancellationToken);
    }
}
