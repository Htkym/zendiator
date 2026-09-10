using Zendiator;
using Zendiator.DependencyInjection;

namespace Zendiator.DiConfiguration.Tests;

public sealed class DiMarker;

public sealed class Trace
{
    public List<string> Events { get; } = [];
}

public sealed class DiBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public static int Calls;
    public ValueTask<TResponse> HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        Interlocked.Increment(ref Calls);
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class DiVoidBehavior<TRequest> : IPipelineBehavior<TRequest>
    where TRequest : IRequest
{
    public static int Calls;
    public ValueTask HandleAsync<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest>
    {
        Interlocked.Increment(ref Calls);
        return next.InvokeAsync(request, cancellationToken);
    }
}

public sealed class DiSyncBehavior<TRequest, TResponse> : ISyncPipelineBehavior<TRequest, TResponse>
    where TRequest : ISyncRequest<TResponse>, allows ref struct
{
    public static int Calls;
    public TResponse Handle<TNext>(TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, ISyncRequestContinuation<TRequest, TResponse>
    {
        Interlocked.Increment(ref Calls);
        return next.Invoke(request, cancellationToken);
    }
}

public readonly record struct Add(int Left, int Right) : IRequest<int>;

public sealed class AddHandler : IRequestHandler<Add, int>
{
    public ValueTask<int> HandleAsync(Add request, CancellationToken cancellationToken) =>
        new(request.Left + request.Right);
}

public sealed record Clear(int Id) : ICommand;

public sealed class ClearHandler : IRequestHandler<Clear>
{
    public static List<int> Cleared { get; } = [];
    public ValueTask HandleAsync(Clear request, CancellationToken cancellationToken)
    {
        Cleared.Add(request.Id);
        return default;
    }
}

public sealed record Item(int Id);

public sealed record GetById<T>(int Id) : IRequest<T>
    where T : class;

public sealed class GetByIdHandler<T> : IRequestHandler<GetById<T>, T>
    where T : class
{
    public ValueTask<T> HandleAsync(GetById<T> request, CancellationToken cancellationToken) =>
        new((T)(object)new Item(request.Id));
}

public sealed record Changed(int Id) : INotification;

public static class ChangedOrder
{
    public static List<string> Events { get; } = [];
}

public sealed class ChangedFirst : INotificationHandler<Changed>
{
    public static List<int> Seen { get; } = [];
    public ValueTask HandleAsync(Changed notification, CancellationToken cancellationToken)
    {
        Seen.Add(notification.Id);
        ChangedOrder.Events.Add("first:" + notification.Id);
        return default;
    }
}

[HandlerOrder(Order = 0)]
public sealed class ChangedSecond : INotificationHandler<Changed>
{
    public static List<int> Seen { get; } = [];
    public ValueTask HandleAsync(Changed notification, CancellationToken cancellationToken)
    {
        Seen.Add(notification.Id * 10);
        ChangedOrder.Events.Add("second:" + notification.Id);
        return default;
    }
}

[HandlerOrder(Order = 2)]
public sealed class ChangedThird : INotificationHandler<Changed>
{
    public static List<int> Seen { get; } = [];
    public ValueTask HandleAsync(Changed notification, CancellationToken cancellationToken)
    {
        Seen.Add(notification.Id * 100);
        ChangedOrder.Events.Add("third:" + notification.Id);
        return default;
    }
}

public sealed record Lonely : INotification;

public sealed record Pair(int Value) : IMultiRequest<int>;

public sealed class PairHandlerA : IRequestHandler<Pair, int>
{
    public ValueTask<int> HandleAsync(Pair request, CancellationToken cancellationToken) => new(request.Value + 1);
}

[HandlerOrder(Order = 1)]
public sealed class PairHandlerB : IRequestHandler<Pair, int>
{
    public ValueTask<int> HandleAsync(Pair request, CancellationToken cancellationToken) => new(request.Value * 10);
}

public sealed record PingAll(string Message) : IMultiRequest;

public sealed class PingAllHandlerA : IRequestHandler<PingAll>
{
    public static List<string> Got { get; } = [];
    public ValueTask HandleAsync(PingAll request, CancellationToken cancellationToken)
    {
        Got.Add("a:" + request.Message);
        return default;
    }
}

public sealed class PingAllHandlerB : IRequestHandler<PingAll>
{
    public static List<string> Got { get; } = [];
    public ValueTask HandleAsync(PingAll request, CancellationToken cancellationToken)
    {
        Got.Add("b:" + request.Message);
        return default;
    }
}

public readonly ref struct Parse : ISyncRequest<int>
{
    public Parse(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}

public sealed class ParseHandler : ISyncRequestHandler<Parse, int>
{
    public int Handle(scoped Parse request, CancellationToken cancellationToken) => request.Data.Length;
}

public readonly record struct Wipe(int Id) : ISyncRequest;

public sealed class WipeHandler : ISyncRequestHandler<Wipe>
{
    public static List<int> Got { get; } = [];
    public void Handle(Wipe request, CancellationToken cancellationToken) => Got.Add(request.Id);
}

public readonly record struct AddSync(int Value) : ISyncMultiRequest<int>;

public sealed class AddSyncHandlerA : ISyncRequestHandler<AddSync, int>
{
    public int Handle(AddSync request, CancellationToken cancellationToken) => request.Value + 1;
}

public sealed class AddSyncHandlerB : ISyncRequestHandler<AddSync, int>
{
    public int Handle(AddSync request, CancellationToken cancellationToken) => request.Value + 2;
}

public readonly record struct LegacyWork : ICommand;

public sealed class LegacyWorkHandler : ICommandHandler<LegacyWork>
{
    public ValueTask<Unit> HandleAsync(LegacyWork request, CancellationToken cancellationToken) => new(Unit.Value);
}

public sealed record DiNumbers(int Count) : IStreamRequest<int>;

public sealed class DiNumbersHandler : IStreamRequestHandler<DiNumbers, int>
{
    public static int Calls;
    public async IAsyncEnumerable<int> HandleAsync(DiNumbers request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed class DiStreamBehavior<TRequest, TItem> : IStreamPipelineBehavior<TRequest, TItem>
    where TRequest : IStreamRequest<TItem>
{
    public static int Calls;
    public async IAsyncEnumerable<TItem> HandleAsync<TNext>(TRequest request, TNext next, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where TNext : struct, IStreamContinuation<TRequest, TItem>
    {
        Interlocked.Increment(ref Calls);
        await foreach (var item in next.InvokeAsync(request, cancellationToken).WithCancellation(cancellationToken))
            yield return item;
    }
}
