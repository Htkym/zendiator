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

public sealed class HStreamA5B3 : IStreamPipelineBehavior<HStreamA5, int>
{
    public async IAsyncEnumerable<int> HandleAsync<HNext>(HStreamA5 request, HNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
        where HNext : struct, IStreamContinuation<HStreamA5, int>
    {
        await foreach (var item in next.InvokeAsync(request, cancellationToken))
            yield return item;
    }
}
