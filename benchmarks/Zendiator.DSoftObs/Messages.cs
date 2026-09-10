namespace Zendiator.DSoftObs;

// Isolated open post-only behavior (DsO0(41) = 141).

public sealed record DsO0(int Value) : global::DSoftStudio.Mediator.Abstractions.IRequest<int>;

public sealed class DsO0Handler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsO0, int>
{
    public ValueTask<int> Handle(DsO0 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class DsObsBehavior<TMessage, TResponse> : global::DSoftStudio.Mediator.Abstractions.IPipelineBehavior<TMessage, TResponse>
    where TMessage : global::DSoftStudio.Mediator.Abstractions.IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(TMessage request, global::DSoftStudio.Mediator.Abstractions.IRequestHandler<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var downstream = await next.Handle(request, cancellationToken).ConfigureAwait(false);
        return (TResponse)(object)((int)(object)downstream! + 100);
    }
}

public sealed class Marker;
