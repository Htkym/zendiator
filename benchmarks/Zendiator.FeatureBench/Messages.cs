using Zendiator;

namespace Zendiator.FeatureBench;

// Warm, sync-completed shapes for the FeatureExpansion-1 dispatch paths.

[GenerateZendiator]
public sealed partial class Zendiator;

public sealed record ZVoid(int Value) : ICommand;

public sealed class ZVoidHandler : IRequestHandler<ZVoid>
{
    public ValueTask HandleAsync(ZVoid request, CancellationToken cancellationToken) => default;
}

public sealed record ZEv1(int Value) : INotification;

public sealed class ZEv1Handler : INotificationHandler<ZEv1>
{
    public ValueTask HandleAsync(ZEv1 notification, CancellationToken cancellationToken) => default;
}

public sealed record ZEv4(int Value) : INotification;

public sealed class ZEv4HandlerA : INotificationHandler<ZEv4>
{
    public ValueTask HandleAsync(ZEv4 notification, CancellationToken cancellationToken) => default;
}

public sealed class ZEv4HandlerB : INotificationHandler<ZEv4>
{
    public ValueTask HandleAsync(ZEv4 notification, CancellationToken cancellationToken) => default;
}

public sealed class ZEv4HandlerC : INotificationHandler<ZEv4>
{
    public ValueTask HandleAsync(ZEv4 notification, CancellationToken cancellationToken) => default;
}

public sealed class ZEv4HandlerD : INotificationHandler<ZEv4>
{
    public ValueTask HandleAsync(ZEv4 notification, CancellationToken cancellationToken) => default;
}

public sealed record ZMulti(int Value) : IMultiRequest<int>;

public sealed class ZMultiHandlerA : IRequestHandler<ZMulti, int>
{
    public ValueTask<int> HandleAsync(ZMulti request, CancellationToken cancellationToken) => new(request.Value + 1);
}

public sealed class ZMultiHandlerB : IRequestHandler<ZMulti, int>
{
    public ValueTask<int> HandleAsync(ZMulti request, CancellationToken cancellationToken) => new(request.Value + 2);
}

public readonly ref struct ZSpan : ISyncRequest<int>
{
    public ZSpan(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}

public sealed class ZSpanHandler : ISyncRequestHandler<ZSpan, int>
{
    public int Handle(scoped ZSpan request, CancellationToken cancellationToken) => request.Data.Length;
}

public sealed record ZDto(int Value);

public sealed record ZGen<T>(int Value) : IRequest<T>
    where T : class;

public sealed class ZGenHandler<T> : IRequestHandler<ZGen<T>, T>
    where T : class
{
    private readonly Func<int, T> _create;
    public ZGenHandler() => _create = static value => (T)(object)new ZDto(value);
    public ValueTask<T> HandleAsync(ZGen<T> request, CancellationToken cancellationToken) => new(_create(request.Value));
}

/// <summary>Same-meaning direct calls. Never on the measured mediator path.</summary>
public static class ZrFeatureDirect
{
    private static readonly ZVoidHandler VoidHandler = new();
    private static readonly ZEv1Handler Ev1Handler = new();
    private static readonly ZEv4HandlerA Ev4A = new();
    private static readonly ZEv4HandlerB Ev4B = new();
    private static readonly ZEv4HandlerC Ev4C = new();
    private static readonly ZEv4HandlerD Ev4D = new();
    private static readonly ZMultiHandlerA MultiA = new();
    private static readonly ZMultiHandlerB MultiB = new();
    private static readonly ZSpanHandler SpanHandler = new();
    private static readonly ZGenHandler<ZDto> GenHandler = new();
    private static readonly ZStreamHandler StreamHandler = new();
    private static readonly ZStreamYieldHandler StreamYieldHandler = new();

    public static ValueTask DirectVoid(ZVoid request, CancellationToken cancellationToken) =>
        VoidHandler.HandleAsync(request, cancellationToken);

    public static async ValueTask DirectFanout1(ZEv1 notification, CancellationToken cancellationToken) =>
        await Ev1Handler.HandleAsync(notification, cancellationToken).ConfigureAwait(false);

    public static async ValueTask DirectFanout4(ZEv4 notification, CancellationToken cancellationToken)
    {
        await Ev4A.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
        await Ev4B.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
        await Ev4C.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
        await Ev4D.HandleAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<System.Collections.Generic.IReadOnlyList<int>> DirectMulti(ZMulti request, CancellationToken cancellationToken)
    {
        var results = new System.Collections.Generic.List<int>(2)
        {
            await MultiA.HandleAsync(request, cancellationToken).ConfigureAwait(false),
            await MultiB.HandleAsync(request, cancellationToken).ConfigureAwait(false),
        };
        return results;
    }

    public static int DirectSpan(scoped ZSpan request, CancellationToken cancellationToken) =>
        SpanHandler.Handle(request, cancellationToken);

    public static ValueTask<ZDto> DirectGen(ZGen<ZDto> request, CancellationToken cancellationToken) =>
        GenHandler.HandleAsync(request, cancellationToken);

    public static System.Collections.Generic.IAsyncEnumerable<int> DirectStream(ZStream request, CancellationToken cancellationToken) =>
        StreamHandler.HandleAsync(request, cancellationToken);

    public static System.Collections.Generic.IAsyncEnumerable<int> DirectStreamYield(ZStream request, CancellationToken cancellationToken) =>
        StreamYieldHandler.HandleAsync(request, cancellationToken);
}

public sealed record ZStream(int Count) : IStreamRequest<int>;

public sealed class ZStreamHandler : IStreamRequestHandler<ZStream, int>
{
    public async IAsyncEnumerable<int> HandleAsync(ZStream request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
        await Task.CompletedTask;
    }
}

public sealed class ZStreamYieldHandler
{
    // Truly async variant for yield-shape comparison; not registered (duplicate would error).
    // Invoked directly as Direct baseline only.
    public async IAsyncEnumerable<int> HandleAsync(ZStream request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
