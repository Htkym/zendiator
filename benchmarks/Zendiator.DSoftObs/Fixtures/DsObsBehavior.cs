namespace Zendiator.DSoftObs;

public sealed class DsObsBehavior<TMessage, TResponse> : global::DSoftStudio.Mediator.Abstractions.IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::DSoftStudio.Mediator.Abstractions.IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(TMessage request, global::DSoftStudio.Mediator.Abstractions.IRequestHandler<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var downstream = await next.Handle(request, cancellationToken).ConfigureAwait(false);
        return (TResponse)(object)((int)(object)downstream! + 100);
    }
}
