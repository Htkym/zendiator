using global::Zendiator;

namespace Zendiator.Tests;

[GenerateZendiator]
[PipelineBehavior(typeof(Outer<,>), Order = 0)]
[PipelineBehavior(typeof(Middle<,>), Order = 1)]
[PipelineBehavior(typeof(Inner<,>), Order = 2)]
[PipelineBehavior(typeof(Validation), Order = 3)]
[PipelineBehavior(typeof(RetryBehavior), Order = 4)]
[PipelineBehavior(typeof(TokenBehavior), Order = 5)]
[PipelineBehavior(typeof(CommandTracer<>), Order = 6)]
[PipelineBehavior(typeof(GuardBehavior), Order = 7)]
[PipelineBehavior(typeof(FlakyRetryBehavior), Order = 8)]
[PipelineBehavior(typeof(SwapTokenBehavior), Order = 9)]
[PipelineBehavior(typeof(TopUpBehavior), Order = 10)]
[PipelineBehavior(typeof(QuoteTaxBehavior), Order = 11)]
[PipelineBehavior(typeof(VersionBumpBehavior), Order = 12)]
[PipelineBehavior(typeof(MaybeGateBehavior), Order = 13)]
[PipelineBehavior(typeof(BroadcastBehavior), Order = 14)]
[PipelineBehavior(typeof(SyncTrace<,>), Order = 15)]
[PipelineBehavior(typeof(SyncTag<,>), Order = 16)]
[PipelineBehavior(typeof(ParseGateBehavior), Order = 17)]
[PipelineBehavior(typeof(ParseBumpBehavior), Order = 18)]
[PipelineBehavior(typeof(ParseFinalBehavior), Order = 19)]
[PipelineBehavior(typeof(AddOneRetryBehavior), Order = 20)]
[PipelineBehavior(typeof(AddTenSyncBehavior), Order = 21)]
[PipelineBehavior(typeof(SyncSwapBehavior), Order = 22)]
[PipelineBehavior(typeof(SumBumpBehavior), Order = 23)]
[PipelineBehavior(typeof(SyncVoidTrace<>), Order = 24)]
[PipelineBehavior(typeof(StreamTracePiped), Order = 25)]
[PipelineBehavior(typeof(StreamA3), Order = 26)]
[PipelineBehavior(typeof(StreamB3), Order = 27)]
[PipelineBehavior(typeof(StreamC3), Order = 28)]
[PipelineBehavior(typeof(StreamA5), Order = 29)]
[PipelineBehavior(typeof(StreamB5), Order = 30)]
[PipelineBehavior(typeof(StreamC5), Order = 31)]
[PipelineBehavior(typeof(StreamD5), Order = 32)]
[PipelineBehavior(typeof(StreamE5), Order = 33)]
[PipelineBehavior(typeof(StreamTransform), Order = 34)]
[PipelineBehavior(typeof(StreamFilter), Order = 35)]
[PipelineBehavior(typeof(StreamGate), Order = 36)]
[PipelineBehavior(typeof(StreamReplace), Order = 37)]
[PipelineBehavior(typeof(StreamTraceFail), Order = 38)]
public sealed partial class Zendiator;

public sealed class Trace
{
    public bool Enabled { get; set; } = true;
    public List<string> Events { get; } = [];
    public void Add(string value) { if (Enabled) Events.Add(value); }
}

public abstract class Traced<TRequest, TResponse>(Trace trace, string name) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        trace.Add(name);
        try { return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false); }
        finally { trace.Add("/" + name); }
    }
}
public sealed class Outer<TRequest, TResponse>(Trace trace) : Traced<TRequest, TResponse>(trace, "outer") where TRequest : IRequest<TResponse>;
public sealed class Middle<TRequest, TResponse>(Trace trace) : Traced<TRequest, TResponse>(trace, "middle") where TRequest : IRequest<TResponse>;
public sealed class Inner<TRequest, TResponse>(Trace trace) : Traced<TRequest, TResponse>(trace, "inner") where TRequest : IRequest<TResponse>;

public readonly record struct Sum(int Left, int Right) : IQuery<int>;
public sealed class SumHandler(Trace trace) : IQueryHandler<Sum, int>
{
    public ValueTask<int> HandleAsync(Sum request, CancellationToken cancellationToken)
    { trace.Add("handler"); return new(request.Left + request.Right); }
}
public sealed record Echo(string? Value) : IRequest<string?>;
public sealed class EchoHandler : IRequestHandler<Echo, string?>
{
    public ValueTask<string?> HandleAsync(Echo request, CancellationToken cancellationToken) => new(request.Value);
}
public readonly record struct Complete : ICommand;
public sealed class CompleteHandler : ICommandHandler<Complete>
{
    public ValueTask<Unit> HandleAsync(Complete request, CancellationToken cancellationToken) => new(Unit.Value);
}
public sealed record Delayed(Task<int> Pending) : IRequest<int>;
public sealed class DelayedHandler : IRequestHandler<Delayed, int>
{
    public async ValueTask<int> HandleAsync(Delayed request, CancellationToken cancellationToken) =>
        await request.Pending.WaitAsync(cancellationToken).ConfigureAwait(false);
}
public sealed record Fail(Exception Error) : IRequest<int>;
public sealed class FailHandler : IRequestHandler<Fail, int>
{
    public ValueTask<int> HandleAsync(Fail request, CancellationToken cancellationToken) => throw request.Error;
}
public readonly record struct Outcome(bool Success);
public readonly record struct Validate(bool Valid) : ICommand<Outcome>;
public sealed class Validation : IPipelineBehavior<Validate, Outcome>
{
    public ValueTask<Outcome> HandleAsync<TNext>(Validate request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<Validate, Outcome> => request.Valid
            ? next.InvokeAsync(request, cancellationToken) : new(new Outcome(false));
}
public sealed class ValidateHandler : ICommandHandler<Validate, Outcome>
{
    public ValueTask<Outcome> HandleAsync(Validate request, CancellationToken cancellationToken) => new(new Outcome(true));
}
public readonly record struct Retry : IRequest<int>;
public sealed class RetryHandler : IRequestHandler<Retry, int>
{
    private int _calls;
    public ValueTask<int> HandleAsync(Retry request, CancellationToken cancellationToken) => new(++_calls);
}
public sealed class RetryBehavior : IPipelineBehavior<Retry, int>
{
    public async ValueTask<int> HandleAsync<TNext>(Retry request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<Retry, int>
    {
        await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
public sealed record ChangeToken(CancellationToken Replacement) : IRequest<CancellationToken>;
public sealed class TokenBehavior : IPipelineBehavior<ChangeToken, CancellationToken>
{
    public ValueTask<CancellationToken> HandleAsync<TNext>(ChangeToken request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<ChangeToken, CancellationToken> => next.InvokeAsync(request, request.Replacement);
}
public sealed class TokenHandler : IRequestHandler<ChangeToken, CancellationToken>
{
    public ValueTask<CancellationToken> HandleAsync(ChangeToken request, CancellationToken cancellationToken) => new(cancellationToken);
}
public readonly record struct Identity : IRequest<Guid>;
public sealed class IdentityHandler : IRequestHandler<Identity, Guid>, IAsyncDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public bool Disposed { get; private set; }
    public ValueTask<Guid> HandleAsync(Identity request, CancellationToken cancellationToken) => new(Id);
    public ValueTask DisposeAsync() { Disposed = true; return default; }
}

public sealed record DeleteUser(int UserId) : ICommand;

public sealed class DeleteUserHandler(Trace trace) : IRequestHandler<DeleteUser>
{
    public static List<int> Deleted { get; } = [];

    public async ValueTask HandleAsync(DeleteUser command, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        trace.Add($"deleted:{command.UserId}");
        Deleted.Add(command.UserId);
    }
}

public sealed class CommandTracer<TRequest>(Trace trace) : IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public async ValueTask HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest>
    {
        trace.Add("cmd");
        try
        {
            await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            trace.Add("/cmd");
        }
    }
}

public sealed record GuardedDelete(int UserId) : ICommand;

public sealed class GuardedDeleteHandler : IRequestHandler<GuardedDelete>
{
    public static int Calls;
    public ValueTask HandleAsync(GuardedDelete request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return default;
    }
}

public sealed class GuardBehavior : IPipelineBehavior<GuardedDelete>
{
    public ValueTask HandleAsync<TNext>(GuardedDelete request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GuardedDelete> =>
        request.UserId > 0 ? next.InvokeAsync(request, cancellationToken) : default;
}

public sealed record FlakyCommand : ICommand;

public sealed class FlakyHandler : IRequestHandler<FlakyCommand>
{
    public static int Calls;
    public ValueTask HandleAsync(FlakyCommand request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return default;
    }
}

public sealed class FlakyRetryBehavior : IPipelineBehavior<FlakyCommand>
{
    public async ValueTask HandleAsync<TNext>(FlakyCommand request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<FlakyCommand>
    {
        await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record Boom : ICommand;

public sealed class BoomHandler : IRequestHandler<Boom>
{
    public static readonly InvalidOperationException Failure = new("void boom");
    public ValueTask HandleAsync(Boom request, CancellationToken cancellationToken) => throw Failure;
}

public sealed record SeenToken : ICommand;

public sealed class SeenTokenHandler : IRequestHandler<SeenToken>
{
    public static CancellationToken Seen;
    public ValueTask HandleAsync(SeenToken request, CancellationToken cancellationToken)
    {
        Seen = cancellationToken;
        return default;
    }
}

public sealed class SwapTokenBehavior : IPipelineBehavior<SeenToken>
{
    public ValueTask HandleAsync<TNext>(SeenToken request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<SeenToken> =>
        next.InvokeAsync(request, new CancellationTokenSource().Token);
}

public sealed record TopUp(int UserId) : ICommand;

public sealed class TopUpHandler : IRequestHandler<TopUp>
{
    public static int Seen = -1;
    public ValueTask HandleAsync(TopUp request, CancellationToken cancellationToken)
    {
        Seen = request.UserId;
        return default;
    }
}

public sealed class TopUpBehavior : IPipelineBehavior<TopUp>
{
    public ValueTask HandleAsync<TNext>(TopUp request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TopUp> =>
        next.InvokeAsync(new TopUp(request.UserId + 10), cancellationToken);
}

public sealed record LatchCommand(TaskCompletionSource Gate) : ICommand;

public sealed class LatchHandler : IRequestHandler<LatchCommand>
{
    public async ValueTask HandleAsync(LatchCommand request, CancellationToken cancellationToken)
    {
        await request.Gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed record User(int Id, string Name);

public sealed record Product(int Id, string Code);

public interface IRepository<T>
    where T : class
{
    ValueTask<T> GetAsync(int id, CancellationToken cancellationToken);
}

public sealed class UserRepository : IRepository<User>
{
    public static int Calls;
    public ValueTask<User> GetAsync(int id, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new User(id, $"user-{id}"));
    }
}

public sealed class ProductRepository : IRepository<Product>
{
    public static int Calls;
    public ValueTask<Product> GetAsync(int id, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new Product(id, $"code-{id}"));
    }
}

public sealed record GetById<T>(int Id) : IRequest<T>
    where T : class;

public sealed class GetByIdHandler<T>(IRepository<T> repository) : IRequestHandler<GetById<T>, T>
    where T : class
{
    public Guid Id { get; } = Guid.NewGuid();
    public ValueTask<T> HandleAsync(GetById<T> request, CancellationToken cancellationToken) =>
        repository.GetAsync(request.Id, cancellationToken);
}

public sealed record DeleteEntities<T>(int[] Ids) : ICommand
    where T : class;

public sealed class DeleteEntitiesHandler<T> : IRequestHandler<DeleteEntities<T>>
    where T : class
{
    public static List<string> Deleted { get; } = [];
    public ValueTask HandleAsync(DeleteEntities<T> request, CancellationToken cancellationToken)
    {
        foreach (var id in request.Ids) Deleted.Add($"{typeof(T).Name}:{id}");
        return default;
    }
}

public sealed record UserCreated(int UserId) : INotification;

public sealed class AuditLog
{
    public List<string> Events { get; } = [];
}

[HandlerOrder(Order = 1)]
public sealed class WelcomeEmailHandler(AuditLog log) : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"mail:{notification.UserId}");
        return default;
    }
}

[HandlerOrder(Order = -1)]
public sealed class MetricsHandler(AuditLog log) : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"metrics:{notification.UserId}");
        return default;
    }
}

public sealed class ProvisionHandler(AuditLog log) : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"provision:{notification.UserId}");
        return default;
    }
}

[HandlerOrder(Order = 0)]
public sealed class AlphaHandler(AuditLog log) : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
    {
        log.Events.Add("alpha");
        return default;
    }
}

public sealed record Lonely : INotification;

public sealed record SyncPoint(string Name) : INotification;

public sealed class Gate
{
    public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<string> Events { get; } = [];
}

public sealed class FirstGateHandler(Gate gate) : INotificationHandler<SyncPoint>
{
    public async ValueTask HandleAsync(SyncPoint notification, CancellationToken cancellationToken)
    {
        gate.Events.Add("first-start");
        gate.FirstEntered.TrySetResult();
        await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        gate.Events.Add("first-end");
    }
}

[HandlerOrder(Order = 1)]
public sealed class SecondGateHandler(Gate gate) : INotificationHandler<SyncPoint>
{
    public ValueTask HandleAsync(SyncPoint notification, CancellationToken cancellationToken)
    {
        gate.Events.Add("second");
        return default;
    }
}

public sealed record Fragile(int Id) : INotification;

public sealed class FragileFirstHandler : INotificationHandler<Fragile>
{
    public static readonly InvalidOperationException Failure = new("fragile boom");
    public ValueTask HandleAsync(Fragile notification, CancellationToken cancellationToken) => throw Failure;
}

public sealed class FragileSecondHandler : INotificationHandler<Fragile>
{
    public static int Calls;
    public ValueTask HandleAsync(Fragile notification, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return default;
    }
}

public sealed record CancelMid : INotification;

public sealed class CancelFirstHandler : INotificationHandler<CancelMid>
{
    public static CancellationTokenSource? Live;
    public ValueTask HandleAsync(CancelMid notification, CancellationToken cancellationToken)
    {
        Live?.Cancel();
        return default;
    }
}

public sealed class CancelSecondHandler : INotificationHandler<CancelMid>
{
    public static int Calls;
    public ValueTask HandleAsync(CancelMid notification, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return default;
    }
}

public readonly record struct Tick(int N) : INotification;

public sealed class TickHandler : INotificationHandler<Tick>
{
    public static int Sum;
    public ValueTask HandleAsync(Tick notification, CancellationToken cancellationToken)
    {
        Interlocked.Add(ref Sum, notification.N);
        return default;
    }
}

public record BaseNote(int Id) : INotification;

public record DerivedNote(int Id, string Extra) : BaseNote(Id);

public sealed class BaseNoteHandler(AuditLog log) : INotificationHandler<BaseNote>
{
    public ValueTask HandleAsync(BaseNote notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"base:{notification.Id}");
        return default;
    }
}

public sealed class DerivedNoteHandler(AuditLog log) : INotificationHandler<DerivedNote>
{
    public ValueTask HandleAsync(DerivedNote notification, CancellationToken cancellationToken)
    {
        log.Events.Add($"derived:{notification.Id}:{notification.Extra}");
        return default;
    }
}

public sealed record Changed<T>(T Value) : INotification;

public sealed class ChangedHandler<T> : INotificationHandler<Changed<T>>
{
    public static List<string?> Seen { get; } = [];
    public ValueTask HandleAsync(Changed<T> notification, CancellationToken cancellationToken)
    {
        Seen.Add(notification.Value?.ToString());
        return default;
    }
}

public sealed record Quote(string Vendor, decimal Price);

public sealed record GetQuotes(string ProductCode) : IMultiRequest<Quote>;

public sealed class VendorAQuotesHandler : IRequestHandler<GetQuotes, Quote>
{
    public static int Calls;
    public ValueTask<Quote> HandleAsync(GetQuotes request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new Quote("A", 100m));
    }
}

public sealed class VendorCQuotesHandler : IRequestHandler<GetQuotes, Quote>
{
    public static int Calls;
    public ValueTask<Quote> HandleAsync(GetQuotes request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new Quote("C", 150m));
    }
}

[HandlerOrder(Order = 1)]
public sealed class VendorBQuotesHandler : IRequestHandler<GetQuotes, Quote>
{
    public static int Calls;
    public ValueTask<Quote> HandleAsync(GetQuotes request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(new Quote("B", 200m));
    }
}

public sealed class QuoteTaxBehavior : IPipelineBehavior<GetQuotes, Quote>
{
    public async ValueTask<Quote> HandleAsync<TNext>(GetQuotes request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GetQuotes, Quote>
    {
        var quote = await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        return quote with { Price = quote.Price * 1.1m };
    }
}

public sealed record TraceLog
{
    public List<string> Entries { get; } = [];
}

public sealed record GetTrace(TraceLog Log) : IMultiRequest<string>;

public sealed class FirstTraceHandler : IRequestHandler<GetTrace, string>
{
    public ValueTask<string> HandleAsync(GetTrace request, CancellationToken cancellationToken)
    {
        request.Log.Entries.Add("first");
        return new("r1");
    }
}

public sealed class SecondTraceHandler : IRequestHandler<GetTrace, string>
{
    public ValueTask<string> HandleAsync(GetTrace request, CancellationToken cancellationToken)
    {
        request.Log.Entries.Add("second");
        return new("r2");
    }
}

public sealed record GetVersion(int V) : IMultiRequest<int>;

public sealed class VersionBumpBehavior : IPipelineBehavior<GetVersion, int>
{
    public ValueTask<int> HandleAsync<TNext>(GetVersion request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GetVersion, int> =>
        next.InvokeAsync(new GetVersion(request.V + 100), cancellationToken);
}

public sealed class VersionHandlerA : IRequestHandler<GetVersion, int>
{
    public static int SeenA = -1;
    public ValueTask<int> HandleAsync(GetVersion request, CancellationToken cancellationToken)
    {
        SeenA = request.V;
        return new(request.V);
    }
}

public sealed class VersionHandlerB : IRequestHandler<GetVersion, int>
{
    public static int SeenB = -1;
    public ValueTask<int> HandleAsync(GetVersion request, CancellationToken cancellationToken)
    {
        SeenB = request.V;
        return new(request.V);
    }
}

public sealed record GetMaybe(bool Open) : IMultiRequest<int>;

public sealed class MaybeGateBehavior : IPipelineBehavior<GetMaybe, int>
{
    public ValueTask<int> HandleAsync<TNext>(GetMaybe request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<GetMaybe, int> =>
        request.Open ? next.InvokeAsync(request, cancellationToken) : new(0);
}

public sealed class MaybeHandlerA : IRequestHandler<GetMaybe, int>
{
    public static int Calls;
    public ValueTask<int> HandleAsync(GetMaybe request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(1);
    }
}

public sealed class MaybeHandlerB : IRequestHandler<GetMaybe, int>
{
    public static int Calls;
    public ValueTask<int> HandleAsync(GetMaybe request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(2);
    }
}

public sealed record GetRisky : IMultiRequest<int>;

public sealed class RiskyFirstHandler : IRequestHandler<GetRisky, int>
{
    public static readonly InvalidOperationException Failure = new("risky boom");
    public ValueTask<int> HandleAsync(GetRisky request, CancellationToken cancellationToken) => throw Failure;
}

public sealed class RiskySecondHandler : IRequestHandler<GetRisky, int>
{
    public static int Calls;
    public ValueTask<int> HandleAsync(GetRisky request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return new(2);
    }
}

public sealed record Broadcast(string Message) : IMultiRequest;

public sealed class BroadcastHandlerA : IRequestHandler<Broadcast>
{
    public static List<string> Got { get; } = [];
    public ValueTask HandleAsync(Broadcast request, CancellationToken cancellationToken)
    {
        Got.Add($"a:{request.Message}");
        return default;
    }
}

public sealed class BroadcastHandlerB : IRequestHandler<Broadcast>
{
    public static List<string> Got { get; } = [];
    public ValueTask HandleAsync(Broadcast request, CancellationToken cancellationToken)
    {
        Got.Add($"b:{request.Message}");
        return default;
    }
}

public sealed class BroadcastBehavior : IPipelineBehavior<Broadcast>
{
    public static int Calls;
    public ValueTask HandleAsync<TNext>(Broadcast request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<Broadcast>
    {
        Interlocked.Increment(ref Calls);
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed record FanOut<T>(int Id) : IMultiRequest<string>;

public sealed record GetSolo(int Id) : IMultiRequest<int>;

public sealed class SoloHandler : IRequestHandler<GetSolo, int>
{
    public ValueTask<int> HandleAsync(GetSolo request, CancellationToken cancellationToken) => new(request.Id * 2);
}
public sealed class FanOutHandlerA<T> : IRequestHandler<FanOut<T>, string>
{
    public ValueTask<string> HandleAsync(FanOut<T> request, CancellationToken cancellationToken) => new($"a:{request.Id}");
}

public sealed class FanOutHandlerB<T> : IRequestHandler<FanOut<T>, string>
{
    public ValueTask<string> HandleAsync(FanOut<T> request, CancellationToken cancellationToken) => new($"b:{request.Id}");
}

public sealed record Wipe<T>(int[] Ids) : IMultiRequest;

public sealed class WipeHandlerA<T> : IRequestHandler<Wipe<T>>
{
    public static List<int> Got { get; } = [];
    public ValueTask HandleAsync(Wipe<T> request, CancellationToken cancellationToken)
    {
        Got.AddRange(request.Ids);
        return default;
    }
}

public sealed class WipeHandlerB<T> : IRequestHandler<Wipe<T>>
{
    public static List<int> Got { get; } = [];
    public ValueTask HandleAsync(Wipe<T> request, CancellationToken cancellationToken)
    {
        Got.AddRange(request.Ids);
        return default;
    }
}

public readonly ref struct ParseRequest : ISyncRequest<int>
{
    public ParseRequest(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}

public sealed class ParseHandler : ISyncRequestHandler<ParseRequest, int>
{
    public static int Calls;
    public int Handle(scoped ParseRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return request.Data.Length;
    }
}

public sealed class SyncTrace<TRequest, TResponse>(Trace trace) : ISyncPipelineBehavior<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
    where TResponse : struct
{
    public TResponse Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest, TResponse>
    {
        trace.Add("sync");
        try
        {
            return next.Invoke(request, cancellationToken);
        }
        finally
        {
            trace.Add("/sync");
        }
    }
}

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

public sealed class ParseGateBehavior : ISyncPipelineBehavior<ParseRequest, int>
{
    public int Handle<TNext>(scoped ParseRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<ParseRequest, int> =>
        request.Data.Length > 0 ? next.Invoke(request, cancellationToken) : -1;
}

public sealed class ParseBumpBehavior : ISyncPipelineBehavior<ParseRequest, int>
{
    public int Handle<TNext>(ParseRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<ParseRequest, int> =>
        next.Invoke(request, cancellationToken) + 1000;
}

public sealed class ParseFinalBehavior(Trace trace) : ISyncPipelineBehavior<ParseRequest, int>
{
    public int Handle<TNext>(ParseRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<ParseRequest, int>
    {
        trace.Add("final");
        try
        {
            return next.Invoke(request, cancellationToken);
        }
        finally
        {
            trace.Add("/final");
        }
    }
}

public readonly record struct FlushRequest(int Id) : ISyncRequest;

public sealed class FlushHandler : ISyncRequestHandler<FlushRequest>
{
    public static List<int> Flushed { get; } = [];
    public void Handle(FlushRequest request, CancellationToken cancellationToken) => Flushed.Add(request.Id);
}

public sealed class SyncVoidTrace<TRequest>(Trace trace) : ISyncPipelineBehavior<TRequest>
    where TRequest : ISyncRequest, allows ref struct
{
    public void Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest>
    {
        trace.Add("sync-void");
        try
        {
            next.Invoke(request, cancellationToken);
        }
        finally
        {
            trace.Add("/sync-void");
        }
    }
}

public readonly record struct AddOne(int Value) : ISyncRequest<int>;

public sealed class AddOneHandler : ISyncRequestHandler<AddOne, int>
{
    public static int Calls;
    public Guid Id { get; } = Guid.NewGuid();
    public int Handle(AddOne request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return request.Value + 1;
    }
}

public sealed class AddOneRetryBehavior : ISyncPipelineBehavior<AddOne, int>
{
    public int Handle<TNext>(AddOne request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<AddOne, int>
    {
        next.Invoke(request, cancellationToken);
        return next.Invoke(request, cancellationToken);
    }
}

public sealed class AddTenSyncBehavior : ISyncPipelineBehavior<AddOne, int>
{
    public int Handle<TNext>(AddOne request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<AddOne, int> =>
        next.Invoke(new AddOne(request.Value + 10), cancellationToken);
}

public readonly record struct SeenSync : ISyncRequest;

public sealed class SeenSyncHandler : ISyncRequestHandler<SeenSync>
{
    public static CancellationToken Seen;
    public void Handle(SeenSync request, CancellationToken cancellationToken) => Seen = cancellationToken;
}

public sealed class SyncSwapBehavior : ISyncPipelineBehavior<SeenSync>
{
    public void Handle<TNext>(SeenSync request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<SeenSync> =>
        next.Invoke(request, new CancellationTokenSource().Token);
}

public readonly record struct ExplicitSync : ISyncRequest<int>;

public sealed class ExplicitSyncHandler : ISyncRequestHandler<ExplicitSync, int>
{
    int ISyncRequestHandler<ExplicitSync, int>.Handle(ExplicitSync request, CancellationToken cancellationToken) => 7;
}

public readonly record struct GetLabel(int Id) : ISyncRequest<string>;

public sealed class GetLabelHandler : ISyncRequestHandler<GetLabel, string>
{
    public string Handle(GetLabel request, CancellationToken cancellationToken) => $"label-{request.Id}";
}

public readonly ref struct Box<T> : ISyncRequest<int>
    where T : allows ref struct
{
    public Box(T value) => Value = value;
    public readonly T Value;
}

public sealed class BoxHandler<T> : ISyncRequestHandler<Box<T>, int>
    where T : allows ref struct
{
    public int Handle(scoped Box<T> request, CancellationToken cancellationToken) => typeof(T).Name.Length;
}

public readonly record struct SumSync(int A, int B) : ISyncMultiRequest<int>;

public sealed class SumSyncHandlerA : ISyncRequestHandler<SumSync, int>
{
    public int Handle(SumSync request, CancellationToken cancellationToken) => request.A + request.B;
}

[HandlerOrder(Order = 1)]
public sealed class SumSyncHandlerB : ISyncRequestHandler<SumSync, int>
{
    public int Handle(SumSync request, CancellationToken cancellationToken) => request.A * request.B;
}

public sealed class SumBumpBehavior : ISyncPipelineBehavior<SumSync, int>
{
    public int Handle<TNext>(SumSync request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<SumSync, int> =>
        next.Invoke(request, cancellationToken) + 1;
}

public readonly record struct ResetSync(string Name) : ISyncMultiRequest;

public sealed class ResetSyncHandlerA : ISyncRequestHandler<ResetSync>
{
    public static List<string> Got { get; } = [];
    public void Handle(ResetSync request, CancellationToken cancellationToken) => Got.Add($"a:{request.Name}");
}

public sealed class ResetSyncHandlerB : ISyncRequestHandler<ResetSync>
{
    public static List<string> Got { get; } = [];
    public void Handle(ResetSync request, CancellationToken cancellationToken) => Got.Add($"b:{request.Name}");
}

public readonly ref struct MultiRow<T> : ISyncMultiRequest<string>
    where T : allows ref struct
{
    public MultiRow(int id, T value)
    {
        Id = id;
        Value = value;
    }
    public readonly int Id;
    public readonly T Value;
}

public sealed class MultiRowHandlerA<T> : ISyncRequestHandler<MultiRow<T>, string>
    where T : allows ref struct
{
    public string Handle(scoped MultiRow<T> request, CancellationToken cancellationToken) => $"a:{request.Id}";
}

public sealed class MultiRowHandlerB<T> : ISyncRequestHandler<MultiRow<T>, string>
    where T : allows ref struct
{
    public string Handle(scoped MultiRow<T> request, CancellationToken cancellationToken) => $"b:{request.Id}";
}
