using Zendiator;

namespace Zendiator.FeatureBench;

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
