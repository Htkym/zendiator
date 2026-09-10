// Hardening-1 formal stream matrix: Direct vs Zendiator StreamAsync.
// Items 0/1/16/1024/16384, behaviors 0/1/3/5, sync/async yield,
// creation/first/early-break/token boundaries, generic stream.
// Formal: Throughput, LaunchCount 3, MemoryDiagnoser. No HTTP/DB/disk.
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Zendiator;

namespace Zendiator.FeatureBench;

/// <summary>Same-meaning direct calls for the hardening matrix. Never on the mediator path.</summary>
public static class HStreamDirect
{
    private static readonly HStream0Handler S0 = new();
    private static readonly HStreamA0Handler SA0 = new();
    private static readonly HGenStreamHandler<HGenMarker> Gen = new();

    public static IAsyncEnumerable<int> Stream(HStream0 request, CancellationToken cancellationToken) =>
        S0.HandleAsync(request, cancellationToken);

    public static IAsyncEnumerable<int> StreamAsync(HStreamA0 request, CancellationToken cancellationToken) =>
        SA0.HandleAsync(request, cancellationToken);

    public static IAsyncEnumerable<int> GenStream(HGenStream<HGenMarker> request, CancellationToken cancellationToken) =>
        Gen.HandleAsync(request, cancellationToken);
}
