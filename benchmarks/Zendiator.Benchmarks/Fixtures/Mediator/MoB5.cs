namespace Zendiator.Benchmarks;

public sealed class MoB5<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker5, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B5++;
        return next(message, cancellationToken);
    }
}
