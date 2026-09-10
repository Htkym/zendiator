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

public sealed class HStream1Handler : IStreamRequestHandler<HStream1, int>
{
    public async IAsyncEnumerable<int> HandleAsync(HStream1 request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
            yield return i;
        await Task.CompletedTask;
    }
}
