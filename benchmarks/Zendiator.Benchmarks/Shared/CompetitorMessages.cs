namespace Zendiator.Benchmarks;

// Competitor messages/handlers/behaviors, each computing request.Value + 1.

// ---------- MediatR (class records, Task-based) ----------

public interface IMrMarker1;
public interface IMrMarker2;
public interface IMrMarker3;
public interface IMrMarker4;
public interface IMrMarker5;

public sealed record MrPing(int Value) : global::MediatR.IRequest<int>;
public sealed record MrP0(int Value) : global::MediatR.IRequest<int>;
public sealed record MrP1(int Value) : global::MediatR.IRequest<int>, IMrMarker1;
public sealed record MrP3(int Value) : global::MediatR.IRequest<int>, IMrMarker1, IMrMarker2, IMrMarker3;
public sealed record MrP5(int Value) : global::MediatR.IRequest<int>, IMrMarker1, IMrMarker2, IMrMarker3, IMrMarker4, IMrMarker5;

public sealed class MrPingHandler : global::MediatR.IRequestHandler<MrPing, int>
{
    public Task<int> Handle(MrPing request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}

public sealed class MrP0Handler : global::MediatR.IRequestHandler<MrP0, int>
{
    public Task<int> Handle(MrP0 request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}

public sealed class MrP1Handler : global::MediatR.IRequestHandler<MrP1, int>
{
    public Task<int> Handle(MrP1 request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}

public sealed class MrP3Handler : global::MediatR.IRequestHandler<MrP3, int>
{
    public Task<int> Handle(MrP3 request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}

public sealed class MrP5Handler : global::MediatR.IRequestHandler<MrP5, int>
{
    public Task<int> Handle(MrP5 request, CancellationToken cancellationToken) => Task.FromResult(request.Value + 1);
}

public sealed record MrAsyncPing(int Value) : global::MediatR.IRequest<int>;

public sealed class MrAsyncPingHandler : global::MediatR.IRequestHandler<MrAsyncPing, int>
{
    public async Task<int> Handle(MrAsyncPing request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}

public static class MrCounters
{
    public static long B1;
    public static long B2;
    public static long B3;
    public static long B4;
    public static long B5;

    public static void Reset() => B1 = B2 = B3 = B4 = B5 = 0;
}

public sealed class MrB1<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker1
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B1++;
        return next();
    }
}

public sealed class MrB2<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker2
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B2++;
        return next();
    }
}

public sealed class MrB3<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker3
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B3++;
        return next();
    }
}

public sealed class MrB4<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker4
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B4++;
        return next();
    }
}

public sealed class MrB5<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : IMrMarker5
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        MrCounters.B5++;
        return next();
    }
}

public sealed record MrEvent(int Value) : global::MediatR.INotification;

public static class MrEventCounters
{
    public static long H1;
    public static long H4;
    public static long H16;

    public static void Reset() => H1 = H4 = H16 = 0;
}

public sealed record MrEv1(int Value) : global::MediatR.INotification;
public sealed record MrEv4(int Value) : global::MediatR.INotification;

public sealed class MrEv1Handler : global::MediatR.INotificationHandler<MrEv1>
{
    public Task Handle(MrEv1 notification, CancellationToken cancellationToken)
    {
        MrEventCounters.H1++;
        return Task.CompletedTask;
    }
}

public sealed class MrEv4HandlerA : global::MediatR.INotificationHandler<MrEv4>
{
    public Task Handle(MrEv4 notification, CancellationToken cancellationToken)
    {
        MrEventCounters.H4++;
        return Task.CompletedTask;
    }
}

public sealed class MrEv4HandlerB : global::MediatR.INotificationHandler<MrEv4>
{
    public Task Handle(MrEv4 notification, CancellationToken cancellationToken)
    {
        MrEventCounters.H4++;
        return Task.CompletedTask;
    }
}

public sealed class MrEv4HandlerC : global::MediatR.INotificationHandler<MrEv4>
{
    public Task Handle(MrEv4 notification, CancellationToken cancellationToken)
    {
        MrEventCounters.H4++;
        return Task.CompletedTask;
    }
}

public sealed class MrEv4HandlerD : global::MediatR.INotificationHandler<MrEv4>
{
    public Task Handle(MrEv4 notification, CancellationToken cancellationToken)
    {
        MrEventCounters.H4++;
        return Task.CompletedTask;
    }
}

// ---------- martinothamar/Mediator (class records, ValueTask) ----------

public interface IMoMarker1;
public interface IMoMarker2;
public interface IMoMarker3;
public interface IMoMarker4;
public interface IMoMarker5;

public sealed record MoPing(int Value) : global::Mediator.IRequest<int>;
public sealed record MoP0(int Value) : global::Mediator.IRequest<int>;
public sealed record MoP1(int Value) : global::Mediator.IRequest<int>, IMoMarker1;
public sealed record MoP3(int Value) : global::Mediator.IRequest<int>, IMoMarker1, IMoMarker2, IMoMarker3;
public sealed record MoP5(int Value) : global::Mediator.IRequest<int>, IMoMarker1, IMoMarker2, IMoMarker3, IMoMarker4, IMoMarker5;

public sealed class MoPingHandler : global::Mediator.IRequestHandler<MoPing, int>
{
    public ValueTask<int> Handle(MoPing request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class MoP0Handler : global::Mediator.IRequestHandler<MoP0, int>
{
    public ValueTask<int> Handle(MoP0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class MoP1Handler : global::Mediator.IRequestHandler<MoP1, int>
{
    public ValueTask<int> Handle(MoP1 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class MoP3Handler : global::Mediator.IRequestHandler<MoP3, int>
{
    public ValueTask<int> Handle(MoP3 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class MoP5Handler : global::Mediator.IRequestHandler<MoP5, int>
{
    public ValueTask<int> Handle(MoP5 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed record MoAsyncPing(int Value) : global::Mediator.IRequest<int>;

public sealed class MoAsyncPingHandler : global::Mediator.IRequestHandler<MoAsyncPing, int>
{
    public async ValueTask<int> Handle(MoAsyncPing request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return request.Value + 1;
    }
}

public static class MoCounters
{
    public static long B1;
    public static long B2;
    public static long B3;
    public static long B4;
    public static long B5;

    public static void Reset() => B1 = B2 = B3 = B4 = B5 = 0;
}

public sealed class MoB1<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker1, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B1++;
        return next(message, cancellationToken);
    }
}

public sealed class MoB2<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker2, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B2++;
        return next(message, cancellationToken);
    }
}

public sealed class MoB3<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker3, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B3++;
        return next(message, cancellationToken);
    }
}

public sealed class MoB4<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker4, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B4++;
        return next(message, cancellationToken);
    }
}

public sealed class MoB5<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoMarker5, global::Mediator.IMessage
{
    public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        MoCounters.B5++;
        return next(message, cancellationToken);
    }
}

public static class MoEventCounters
{
    public static long H1;
    public static long H4;
    public static long H16;

    public static void Reset() => H1 = H4 = H16 = 0;
}

public sealed record MoEv1(int Value) : global::Mediator.INotification;
public sealed record MoEv4(int Value) : global::Mediator.INotification;

public sealed class MoEv1Handler : global::Mediator.INotificationHandler<MoEv1>
{
    public ValueTask Handle(MoEv1 notification, CancellationToken cancellationToken)
    {
        MoEventCounters.H1++;
        return default;
    }
}

public sealed class MoEv4HandlerA : global::Mediator.INotificationHandler<MoEv4>
{
    public ValueTask Handle(MoEv4 notification, CancellationToken cancellationToken)
    {
        MoEventCounters.H4++;
        return default;
    }
}

public sealed class MoEv4HandlerB : global::Mediator.INotificationHandler<MoEv4>
{
    public ValueTask Handle(MoEv4 notification, CancellationToken cancellationToken)
    {
        MoEventCounters.H4++;
        return default;
    }
}

public sealed class MoEv4HandlerC : global::Mediator.INotificationHandler<MoEv4>
{
    public ValueTask Handle(MoEv4 notification, CancellationToken cancellationToken)
    {
        MoEventCounters.H4++;
        return default;
    }
}

public sealed class MoEv4HandlerD : global::Mediator.INotificationHandler<MoEv4>
{
    public ValueTask Handle(MoEv4 notification, CancellationToken cancellationToken)
    {
        MoEventCounters.H4++;
        return default;
    }
}

// Cross-lane observable: echo handler, +100 per layer. int responses only;
// closed behaviors would be ignored by this library's pipeline.

public interface IMoOb1;
public interface IMoOb2;
public interface IMoOb3;
public interface IMoOb4;
public interface IMoOb5;

public sealed record MoO0(int Value) : global::Mediator.IRequest<int>;
public sealed record MoO1(int Value) : global::Mediator.IRequest<int>, IMoOb1;
public sealed record MoO3(int Value) : global::Mediator.IRequest<int>, IMoOb1, IMoOb2, IMoOb3;
public sealed record MoO5(int Value) : global::Mediator.IRequest<int>, IMoOb1, IMoOb2, IMoOb3, IMoOb4, IMoOb5;

public sealed class MoO0Handler : global::Mediator.IRequestHandler<MoO0, int>
{
    public ValueTask<int> Handle(MoO0 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class MoO1Handler : global::Mediator.IRequestHandler<MoO1, int>
{
    public ValueTask<int> Handle(MoO1 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class MoO3Handler : global::Mediator.IRequestHandler<MoO3, int>
{
    public ValueTask<int> Handle(MoO3 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class MoO5Handler : global::Mediator.IRequestHandler<MoO5, int>
{
    public ValueTask<int> Handle(MoO5 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class MoOb1<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoOb1, global::Mediator.IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var downstream = await next(message, cancellationToken).ConfigureAwait(false);
        return (TResponse)(object)((int)(object)downstream! + 100);
    }
}

public sealed class MoOb2<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoOb2, global::Mediator.IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var downstream = await next(message, cancellationToken).ConfigureAwait(false);
        return (TResponse)(object)((int)(object)downstream! + 100);
    }
}

public sealed class MoOb3<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoOb3, global::Mediator.IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var downstream = await next(message, cancellationToken).ConfigureAwait(false);
        return (TResponse)(object)((int)(object)downstream! + 100);
    }
}

public sealed class MoOb4<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoOb4, global::Mediator.IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var downstream = await next(message, cancellationToken).ConfigureAwait(false);
        return (TResponse)(object)((int)(object)downstream! + 100);
    }
}

public sealed class MoOb5<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMoOb5, global::Mediator.IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var downstream = await next(message, cancellationToken).ConfigureAwait(false);
        return (TResponse)(object)((int)(object)downstream! + 100);
    }
}

