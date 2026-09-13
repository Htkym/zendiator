using global::Zendiator;

namespace Zendiator.Tests;

public sealed class SyncTag<TRequest, TResponse> : ISyncPipelineBehavior<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
    where TResponse : struct
{
    private readonly Trace _trace;
    public SyncTag(Trace trace) => _trace = trace;
    public TResponse Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest, TResponse>
    {
        _trace.Add("tag");
        try
        {
            return next.Invoke(request, cancellationToken);
        }
        finally
        {
            _trace.Add("/tag");
        }
    }
}
