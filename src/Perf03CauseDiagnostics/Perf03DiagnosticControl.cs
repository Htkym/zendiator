using NativeEntry = Competitive.Generated.Perf03NamedStreams.Zendiator;

namespace Competitive.Perf03.Diagnostics;

internal static class Perf03DiagnosticControl
{
    internal static IAsyncEnumerable<int> CreateStream0(NativeEntry services, Stream0 request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return __ZendiatorStreamCanonical_DF7872ACF770B8DDDA6D6A61054B21A2251A08597B558387231219CCDDFB0B4B.Create(services, request, token);
    }

    internal static IAsyncEnumerable<int> CreateRelay5(NativeEntry services, Relay5 request,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        return __ZendiatorStreamCanonical_B80A3F15A7016AF93086943F607702393A9513056C3C751D172215C32167550B.Create(services, request, token);
    }
}
