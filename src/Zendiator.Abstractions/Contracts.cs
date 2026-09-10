namespace Zendiator;

/// <summary>A request with one response type.</summary>
public interface IRequest<TResponse>;

/// <summary>A command with a response.</summary>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>A query with a response.</summary>
public interface IQuery<TResponse> : IRequest<TResponse>;

/// <summary>A request without a meaningful response.</summary>
public interface IRequest : IRequest<Unit>;

/// <summary>A command without a meaningful response.</summary>
public interface ICommand : IRequest, ICommand<Unit>;

/// <summary>The single value returned by commands without a response.</summary>
public readonly record struct Unit
{
    /// <summary>Gets the single unit value.</summary>
    public static Unit Value => default;
}

/// <summary>Handles one request. Cancellation and business errors belong to the handler.</summary>
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request. Await the returned value only once.</summary>
    ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Handles a command with a response.</summary>
public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

/// <summary>Handles a command without a meaningful response (legacy Unit route).</summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Unit> where TCommand : ICommand;

/// <summary>Handles a query.</summary>
public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;

/// <summary>Handles one request without a meaningful response (native void route).</summary>
public interface IRequestHandler<TRequest>
    where TRequest : IRequest
{
    /// <summary>Handles the request. Await the returned value only once.</summary>
    ValueTask HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>A generated, value-type continuation. Do not retain it after the dispatch finishes.</summary>
public interface IRequestContinuation<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    ValueTask<TResponse> InvokeAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Wraps a request. A continuation may be invoked repeatedly, sequentially, or not at all.</summary>
public interface IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>Executes this behavior without boxing or allocating a continuation delegate.</summary>
    ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>;
}

/// <summary>A generated, value-type continuation for requests without a response.</summary>
public interface IRequestContinuation<TRequest>
    where TRequest : IRequest
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    ValueTask InvokeAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Wraps a request without a response.</summary>
public interface IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    /// <summary>Executes this behavior without boxing or allocating a continuation delegate.</summary>
    ValueTask HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest>;
}

/// <summary>A notification delivered to zero or more subscribers in order.</summary>
public interface INotification
{
}

/// <summary>Handles one notification. There is no response and no automatic retry.</summary>
public interface INotificationHandler<TNotification>
    where TNotification : INotification
{
    /// <summary>Handles the notification. Await the returned value only once.</summary>
    ValueTask HandleAsync(TNotification notification, CancellationToken cancellationToken);
}

/// <summary>A request with one response type delivered to multiple handlers.</summary>
public interface IMultiRequest<TResponse> : IRequest<TResponse>
{
}

/// <summary>A request without a response delivered to multiple handlers.</summary>
public interface IMultiRequest : IRequest
{
}

/// <summary>A synchronous request with one response type. May be a ref struct.</summary>
public interface ISyncRequest<TResponse>
{
}

/// <summary>A synchronous request without a response. May be a ref struct.</summary>
public interface ISyncRequest
{
}

/// <summary>A synchronous command without a response. May be a ref struct.</summary>
public interface ISyncCommand : ISyncRequest
{
}

/// <summary>A synchronous multi-handler request with one response type. May be a ref struct.</summary>
public interface ISyncMultiRequest<TResponse> : ISyncRequest<TResponse>
{
}

/// <summary>A synchronous multi-handler request without a response. May be a ref struct.</summary>
public interface ISyncMultiRequest : ISyncRequest
{
}

/// <summary>Handles one synchronous request. The request is passed by scoped argument and must not escape.</summary>
public interface ISyncRequestHandler<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    /// <summary>Handles the request synchronously without retaining it.</summary>
    TResponse Handle(scoped TRequest request, CancellationToken cancellationToken);
}

/// <summary>Handles one synchronous request without a response.</summary>
public interface ISyncRequestHandler<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    /// <summary>Handles the request synchronously without retaining it.</summary>
    void Handle(scoped TRequest request, CancellationToken cancellationToken);
}

/// <summary>A generated, value-type synchronous continuation. Must not retain the request.</summary>
public interface ISyncRequestContinuation<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    TResponse Invoke(scoped TRequest request, CancellationToken cancellationToken);
}

/// <summary>Wraps a synchronous request.</summary>
public interface ISyncPipelineBehavior<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    /// <summary>Executes this behavior without retaining the request.</summary>
    TResponse Handle<TNext>(scoped TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest, TResponse>;
}

/// <summary>A generated, value-type synchronous continuation for requests without a response.</summary>
public interface ISyncRequestContinuation<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    void Invoke(scoped TRequest request, CancellationToken cancellationToken);
}

/// <summary>Wraps a synchronous request without a response.</summary>
public interface ISyncPipelineBehavior<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    /// <summary>Executes this behavior without retaining the request.</summary>
    void Handle<TNext>(scoped TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest>;
}

/// <summary>A streaming request producing zero or more items. One request maps to exactly one stream handler.</summary>
public interface IStreamRequest<TItem>;

/// <summary>Handles one streaming request. The returned enumerable is lazy; do not retain the request after enumeration finishes.</summary>
public interface IStreamRequestHandler<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>Handles the streaming request. Enumeration starts only when the returned enumerable is enumerated.</summary>
    IAsyncEnumerable<TItem> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>A generated, value-type stream continuation. Do not retain it after the enumeration finishes.</summary>
public interface IStreamContinuation<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>Invokes the next node with the supplied request and token.</summary>
    IAsyncEnumerable<TItem> InvokeAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Wraps a streaming request. A continuation should be invoked at most once per enumeration.</summary>
public interface IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    /// <summary>Executes this behavior without boxing or allocating a continuation delegate.</summary>
    IAsyncEnumerable<TItem> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<TRequest, TItem>;
}
