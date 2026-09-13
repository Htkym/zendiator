namespace Zendiator.Benchmarks;

public sealed class MoOb1<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoOb1, global::Mediator.IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var downstream = await next(message, cancellationToken).ConfigureAwait(false);
        return (TResponse)(object)((int)(object)downstream! + 100);
    }
}
