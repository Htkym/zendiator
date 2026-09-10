namespace Zendiator.Benchmarks;

// ---------- DispatchR (class-only requests, ValueTask) ----------

public sealed record DrPing(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrPing, ValueTask<int>>;
public sealed record DrP0(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrP0, ValueTask<int>>;
public sealed record DrP1(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrP1, ValueTask<int>>;
public sealed record DrP3(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrP3, ValueTask<int>>;
public sealed record DrP5(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrP5, ValueTask<int>>;

public sealed class DrPingHandler : global::DispatchR.Abstractions.Send.IRequestHandler<DrPing, ValueTask<int>>
{
    public ValueTask<int> Handle(DrPing request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class DrP0Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrP0, ValueTask<int>>
{
    public ValueTask<int> Handle(DrP0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class DrP1Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrP1, ValueTask<int>>
{
    public ValueTask<int> Handle(DrP1 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class DrP3Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrP3, ValueTask<int>>
{
    public ValueTask<int> Handle(DrP3 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class DrP5Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrP5, ValueTask<int>>
{
    public ValueTask<int> Handle(DrP5 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public static class DrCounters
{
    public static long B1;
    public static long B2;
    public static long B3;
    public static long B4;
    public static long B5;
    public static long B6;
    public static long B7;
    public static long B8;
    public static long B9;

    public static void Reset() => B1 = B2 = B3 = B4 = B5 = B6 = B7 = B8 = B9 = 0;
}

// Closed per-request behaviors (open behaviors are not constrained per request here).
public sealed class DrP1B1 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP1, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP1, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP1 request, CancellationToken cancellationToken)
    {
        DrCounters.B1++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public sealed class DrP3B1 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP3, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP3, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP3 request, CancellationToken cancellationToken)
    {
        DrCounters.B2++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public sealed class DrP3B2 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP3, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP3, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP3 request, CancellationToken cancellationToken)
    {
        DrCounters.B3++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public sealed class DrP3B3 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP3, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP3, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP3 request, CancellationToken cancellationToken)
    {
        DrCounters.B4++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public sealed class DrP5B1 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP5, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP5 request, CancellationToken cancellationToken)
    {
        DrCounters.B5++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public sealed class DrP5B2 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP5, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP5 request, CancellationToken cancellationToken)
    {
        DrCounters.B6++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public sealed class DrP5B3 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP5, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP5 request, CancellationToken cancellationToken)
    {
        DrCounters.B7++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public sealed class DrP5B4 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP5, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP5 request, CancellationToken cancellationToken)
    {
        DrCounters.B8++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public sealed class DrP5B5 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrP5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrP5, ValueTask<int>> NextPipeline { get; set; }

    public ValueTask<int> Handle(DrP5 request, CancellationToken cancellationToken)
    {
        DrCounters.B9++;
        return NextPipeline.Handle(request, cancellationToken);
    }
}

public static class DrEventCounters
{
    public static long H1;
    public static long H4;

    public static void Reset() => H1 = H4 = 0;
}

public sealed record DrEv1(int Value) : global::DispatchR.Abstractions.Notification.INotification;
public sealed record DrEv4(int Value) : global::DispatchR.Abstractions.Notification.INotification;

public sealed class DrEv1Handler : global::DispatchR.Abstractions.Notification.INotificationHandler<DrEv1>
{
    public ValueTask Handle(DrEv1 notification, CancellationToken cancellationToken)
    {
        DrEventCounters.H1++;
        return ValueTask.CompletedTask;
    }
}

public sealed class DrEv4HandlerA : global::DispatchR.Abstractions.Notification.INotificationHandler<DrEv4>
{
    public ValueTask Handle(DrEv4 notification, CancellationToken cancellationToken)
    {
        DrEventCounters.H4++;
        return ValueTask.CompletedTask;
    }
}

public sealed class DrEv4HandlerB : global::DispatchR.Abstractions.Notification.INotificationHandler<DrEv4>
{
    public ValueTask Handle(DrEv4 notification, CancellationToken cancellationToken)
    {
        DrEventCounters.H4++;
        return ValueTask.CompletedTask;
    }
}

public sealed class DrEv4HandlerC : global::DispatchR.Abstractions.Notification.INotificationHandler<DrEv4>
{
    public ValueTask Handle(DrEv4 notification, CancellationToken cancellationToken)
    {
        DrEventCounters.H4++;
        return ValueTask.CompletedTask;
    }
}

public sealed class DrEv4HandlerD : global::DispatchR.Abstractions.Notification.INotificationHandler<DrEv4>
{
    public ValueTask Handle(DrEv4 notification, CancellationToken cancellationToken)
    {
        DrEventCounters.H4++;
        return ValueTask.CompletedTask;
    }
}

// ---------- DSoftStudio.Mediator (MediatR-style, ValueTask) ----------

public sealed record DsPing(int Value) : global::DSoftStudio.Mediator.Abstractions.IRequest<int>;
public sealed record DsP0(int Value) : global::DSoftStudio.Mediator.Abstractions.IRequest<int>;

public sealed class DsPingHandler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsPing, int>
{
    public ValueTask<int> Handle(DsPing request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class DsP0Handler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsP0, int>
{
    public ValueTask<int> Handle(DsP0 request, CancellationToken cancellationToken) => new(request.Value + 1);
}

// ---------- Foundatio.Mediator (convention-based) ----------

public record FoPing(int Value);

public sealed class FoPingHandler
{
    public int Handle(FoPing message) => message.Value + 1;
}

// ---------- Immediate.Handlers (generated nested handlers, no mediator object) ----------

public static class IhCounters
{
    public static long B1;
    public static long B2;
    public static long B3;
    public static long B4;
    public static long B5;

    public static void Reset() => B1 = B2 = B3 = B4 = B5 = 0;
}

public sealed class IhB1<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B1++;
        return Next(request, cancellationToken);
    }
}

public sealed class IhB2<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B2++;
        return Next(request, cancellationToken);
    }
}

public sealed class IhB3<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B3++;
        return Next(request, cancellationToken);
    }
}

public sealed class IhB4<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B4++;
        return Next(request, cancellationToken);
    }
}

public sealed class IhB5<TRequest, TResponse> : global::Immediate.Handlers.Shared.Behavior<TRequest, TResponse>
{
    public override ValueTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        IhCounters.B5++;
        return Next(request, cancellationToken);
    }
}

[global::Immediate.Handlers.Shared.Handler]
public static partial class IhPing
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value + 1);
}

[global::Immediate.Handlers.Shared.Handler]
public static partial class IhP0
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value + 1);
}

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhB1<,>))]
public static partial class IhP1
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value + 1);
}

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhB1<,>), typeof(IhB2<,>), typeof(IhB3<,>))]
public static partial class IhP3
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value + 1);
}

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhB1<,>), typeof(IhB2<,>), typeof(IhB3<,>), typeof(IhB4<,>), typeof(IhB5<,>))]
public static partial class IhP5
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value + 1);
}

// ---------- Cross-lane observable: post-only +100 per layer ----------
// Handler echoes Value; O0(v)=v, O1=v+100, O3=v+300, O5=v+500.

public sealed record DrO0(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrO0, ValueTask<int>>;
public sealed record DrO1(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrO1, ValueTask<int>>;
public sealed record DrO3(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrO3, ValueTask<int>>;
public sealed record DrO5(int Value) : global::DispatchR.Abstractions.Send.IRequest<DrO5, ValueTask<int>>;

public sealed class DrO0Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrO0, ValueTask<int>>
{
    public ValueTask<int> Handle(DrO0 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class DrO1Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrO1, ValueTask<int>>
{
    public ValueTask<int> Handle(DrO1 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class DrO3Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrO3, ValueTask<int>>
{
    public ValueTask<int> Handle(DrO3 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class DrO5Handler : global::DispatchR.Abstractions.Send.IRequestHandler<DrO5, ValueTask<int>>
{
    public ValueTask<int> Handle(DrO5 request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class DrO1B1 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO1, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO1, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO1 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class DrO3B1 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO3, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO3, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO3 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class DrO3B2 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO3, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO3, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO3 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class DrO3B3 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO3, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO3, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO3 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class DrO5B1 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO5, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO5 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class DrO5B2 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO5, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO5 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class DrO5B3 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO5, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO5 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class DrO5B4 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO5, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO5 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class DrO5B5 : global::DispatchR.Abstractions.Send.IPipelineBehavior<DrO5, ValueTask<int>>
{
    public required global::DispatchR.Abstractions.Send.IRequestHandler<DrO5, ValueTask<int>> NextPipeline { get; set; }

    public async ValueTask<int> Handle(DrO5 request, CancellationToken cancellationToken)
    {
        var downstream = await NextPipeline.Handle(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed record DsO0(int Value) : global::DSoftStudio.Mediator.Abstractions.IRequest<int>;

public sealed class DsO0Handler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsO0, int>
{
    public ValueTask<int> Handle(DsO0 request, CancellationToken cancellationToken) => new(request.Value);
}

// DSoft open behavior must stay in the isolated assembly (it would apply to DsP0/DsPing here).

public sealed record DsOz0(int Value) : global::DSoftStudio.Mediator.Abstractions.IRequest<int>;

public sealed class DsOz0Handler : global::DSoftStudio.Mediator.Abstractions.IRequestHandler<DsOz0, int>
{
    public ValueTask<int> Handle(DsOz0 request, CancellationToken cancellationToken) => new(request.Value);
}

[global::Immediate.Handlers.Shared.Handler]
public static partial class IhO0
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value);
}

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhO1B1))]
public static partial class IhO1
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value);
}

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhO3B1), typeof(IhO3B2), typeof(IhO3B3))]
public static partial class IhO3
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value);
}

[global::Immediate.Handlers.Shared.Handler]
[global::Immediate.Handlers.Shared.Behaviors(typeof(IhO5B1), typeof(IhO5B2), typeof(IhO5B3), typeof(IhO5B4), typeof(IhO5B5))]
public static partial class IhO5
{
    public record Query(int Value);

    private static ValueTask<int> HandleAsync(Query request, CancellationToken cancellationToken) => new(request.Value);
}

public sealed class IhO1B1 : global::Immediate.Handlers.Shared.Behavior<IhO1.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO1.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class IhO3B1 : global::Immediate.Handlers.Shared.Behavior<IhO3.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO3.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class IhO3B2 : global::Immediate.Handlers.Shared.Behavior<IhO3.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO3.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class IhO3B3 : global::Immediate.Handlers.Shared.Behavior<IhO3.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO3.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class IhO5B1 : global::Immediate.Handlers.Shared.Behavior<IhO5.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO5.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class IhO5B2 : global::Immediate.Handlers.Shared.Behavior<IhO5.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO5.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class IhO5B3 : global::Immediate.Handlers.Shared.Behavior<IhO5.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO5.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class IhO5B4 : global::Immediate.Handlers.Shared.Behavior<IhO5.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO5.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}

public sealed class IhO5B5 : global::Immediate.Handlers.Shared.Behavior<IhO5.Query, int>
{
    public override async ValueTask<int> HandleAsync(IhO5.Query request, CancellationToken cancellationToken)
    {
        var downstream = await Next(request, cancellationToken).ConfigureAwait(false);
        return downstream + 100;
    }
}




