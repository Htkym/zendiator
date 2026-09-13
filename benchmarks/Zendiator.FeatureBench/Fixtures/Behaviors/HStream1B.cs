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

// Passthrough closed behaviors (wrap order verified by construction sequence).
public sealed class HStream1B : IStreamPipelineBehavior<HStream1, int>
{
    public async IAsyncEnumerable<int> HandleAsync<HNext>(HStream1 request, HNext next, [EnumeratorCancellation] CancellationToken cancellationToken)
        where HNext : struct, IStreamContinuation<HStream1, int>
    {
        await foreach (var item in next.InvokeAsync(request, cancellationToken))
            yield return item;
    }
}
