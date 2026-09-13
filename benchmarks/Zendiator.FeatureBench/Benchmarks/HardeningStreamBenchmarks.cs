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

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, launchCount: 3, warmupCount: 3, iterationCount: 8)]
public class HardeningStreamBenchmarks
{
    private readonly HStream0 _s0 = new(0);
    private readonly HStream0 _s1 = new(1);
    private readonly HStream0 _s16 = new(16);
    private readonly HStream0 _s1024 = new(1024);
    private readonly HStream0 _s16384 = new(16384);
    private readonly HStream1 _b1 = new(16);
    private readonly HStream1 _b1k = new(1024);
    private readonly HStream3 _b3 = new(16);
    private readonly HStream3 _b3k = new(1024);
    private readonly HStream5 _b5 = new(16);
    private readonly HStream5 _b5k = new(1024);
    private readonly HStreamA0 _a1024 = new(1024);
    private readonly HStreamA5 _a5k = new(1024);
    private readonly HStreamA0 _a16 = new(16);
    private readonly HGenStream<HGenMarker> _g1024 = new(1024);

    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IZendiator _zr = null!;
    private CancellationTokenSource _apiCts = null!;
    private CancellationTokenSource _enumCts = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _zr = _scope.ServiceProvider.GetRequiredService<IZendiator>();
        _apiCts = new CancellationTokenSource();
        _enumCts = new CancellationTokenSource();
        // Warmup: resolve every measured route once.
        DrainSync(_zr.StreamAsync(_s0)).GetAwaiter().GetResult();
        DrainSync(_zr.StreamAsync(_s1024)).GetAwaiter().GetResult();
        DrainSync(_zr.StreamAsync(_b1k)).GetAwaiter().GetResult();
        DrainSync(_zr.StreamAsync(_b3k)).GetAwaiter().GetResult();
        DrainSync(_zr.StreamAsync(_b5k)).GetAwaiter().GetResult();
        DrainSync(_zr.StreamAsync(_a16).ToBlocking(_enumCts.Token)).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _apiCts.Dispose();
        _enumCts.Dispose();
        _scope.Dispose();
        _provider.Dispose();
    }

    private static async ValueTask<int> DrainSync(IAsyncEnumerable<int> stream)
    {
        var sum = 0;
        await foreach (var i in stream)
            sum += i;
        return sum;
    }

    // Full drain, sync-completing, 0 behaviors.
    [Benchmark(Description = "Zr/0beh/0")]
    public ValueTask<int> Zr0() => DrainSync(_zr.StreamAsync(_s0));
    [Benchmark(Description = "Direct/0beh/0")]
    public ValueTask<int> Direct0() => DrainSync(HStreamDirect.Stream(_s0, CancellationToken.None));
    [Benchmark(Description = "Zr/0beh/1")]
    public ValueTask<int> Zr1() => DrainSync(_zr.StreamAsync(_s1));
    [Benchmark(Description = "Direct/0beh/1")]
    public ValueTask<int> Direct1() => DrainSync(HStreamDirect.Stream(_s1, CancellationToken.None));
    [Benchmark(Description = "Zr/0beh/16")]
    public ValueTask<int> Zr16() => DrainSync(_zr.StreamAsync(_s16));
    [Benchmark(Description = "Direct/0beh/16")]
    public ValueTask<int> Direct16() => DrainSync(HStreamDirect.Stream(_s16, CancellationToken.None));
    [Benchmark(Description = "Zr/0beh/1024")]
    public ValueTask<int> Zr1024() => DrainSync(_zr.StreamAsync(_s1024));
    [Benchmark(Description = "Direct/0beh/1024")]
    public ValueTask<int> Direct1024() => DrainSync(HStreamDirect.Stream(_s1024, CancellationToken.None));
    [Benchmark(Description = "Zr/0beh/16384")]
    public ValueTask<int> Zr16384() => DrainSync(_zr.StreamAsync(_s16384));
    [Benchmark(Description = "Direct/0beh/16384")]
    public ValueTask<int> Direct16384() => DrainSync(HStreamDirect.Stream(_s16384, CancellationToken.None));

    // Behavior scaling, sync-completing.
    [Benchmark(Description = "Zr/1beh/16")]
    public ValueTask<int> ZrB1() => DrainSync(_zr.StreamAsync(_b1));
    [Benchmark(Description = "Zr/1beh/1024")]
    public ValueTask<int> ZrB1K() => DrainSync(_zr.StreamAsync(_b1k));
    [Benchmark(Description = "Zr/3beh/16")]
    public ValueTask<int> ZrB3() => DrainSync(_zr.StreamAsync(_b3));
    [Benchmark(Description = "Zr/3beh/1024")]
    public ValueTask<int> ZrB3K() => DrainSync(_zr.StreamAsync(_b3k));
    [Benchmark(Description = "Zr/5beh/16")]
    public ValueTask<int> ZrB5() => DrainSync(_zr.StreamAsync(_b5));
    [Benchmark(Description = "Zr/5beh/1024")]
    public ValueTask<int> ZrB5K() => DrainSync(_zr.StreamAsync(_b5k));

    // Yield shape, 1024 items.
    [Benchmark(Description = "Zr/async/0beh/1024")]
    public ValueTask<int> ZrA0() => DrainSync(_zr.StreamAsync(_a1024));
    [Benchmark(Description = "Direct/async/1024")]
    public ValueTask<int> DirectA() => DrainSync(HStreamDirect.StreamAsync(_a1024, CancellationToken.None));
    [Benchmark(Description = "Zr/async/5beh/1024")]
    public ValueTask<int> ZrA5K() => DrainSync(_zr.StreamAsync(_a5k));

    // Generic stream, 1024 items.
    [Benchmark(Description = "Zr/generic/1024")]
    public ValueTask<int> ZrGen() => DrainSync(_zr.StreamAsync(_g1024));
    [Benchmark(Description = "Direct/generic/1024")]
    public ValueTask<int> DirectGen() => DrainSync(HStreamDirect.GenStream(_g1024, CancellationToken.None));

    // Boundaries.
    [Benchmark(Description = "Zr/create")]
    public IAsyncEnumerable<int> ZrCreate() => _zr.StreamAsync(_s1024);
    [Benchmark(Description = "Direct/create")]
    public IAsyncEnumerable<int> DirectCreate() => HStreamDirect.Stream(_s1024, CancellationToken.None);

    [Benchmark(Description = "Zr/first")]
    public async ValueTask<int> ZrFirst()
    {
        await using var e = _zr.StreamAsync(_s1024).GetAsyncEnumerator();
        return await e.MoveNextAsync() ? e.Current : -1;
    }

    [Benchmark(Description = "Direct/first")]
    public async ValueTask<int> DirectFirst()
    {
        await using var e = HStreamDirect.Stream(_s1024, CancellationToken.None).GetAsyncEnumerator();
        return await e.MoveNextAsync() ? e.Current : -1;
    }

    [Benchmark(Description = "Zr/break1")]
    public async ValueTask<int> ZrBreak1()
    {
        await foreach (var i in _zr.StreamAsync(_s1024))
            return i;
        return -1;
    }

    [Benchmark(Description = "Direct/break1")]
    public async ValueTask<int> DirectBreak1()
    {
        await foreach (var i in HStreamDirect.Stream(_s1024, CancellationToken.None))
            return i;
        return -1;
    }

    [Benchmark(Description = "Zr/break8")]
    public async ValueTask<int> ZrBreak8()
    {
        var n = 0;
        await foreach (var i in _zr.StreamAsync(_s1024))
        {
            if (++n == 8)
                return i;
        }

        return -1;
    }

    [Benchmark(Description = "Direct/break8")]
    public async ValueTask<int> DirectBreak8()
    {
        var n = 0;
        await foreach (var i in HStreamDirect.Stream(_s1024, CancellationToken.None))
        {
            if (++n == 8)
                return i;
        }

        return -1;
    }

    // Token paths, 16 items.
    [Benchmark(Description = "Zr/tok-none")]
    public ValueTask<int> ZrTokNone() => DrainSync(_zr.StreamAsync(_s16));
    [Benchmark(Description = "Zr/tok-api")]
    public ValueTask<int> ZrTokApi() => DrainSync(_zr.StreamAsync(_s16, _apiCts.Token));
    [Benchmark(Description = "Zr/tok-enum")]
    public ValueTask<int> ZrTokEnum() => DrainSync(_zr.StreamAsync(_s16).ToBlocking(_enumCts.Token));
    [Benchmark(Description = "Zr/tok-same")]
    public ValueTask<int> ZrTokSame() => DrainSync(_zr.StreamAsync(_s16, _apiCts.Token).ToBlocking(_apiCts.Token));
    [Benchmark(Description = "Zr/tok-different")]
    public ValueTask<int> ZrTokDifferent() => DrainSync(_zr.StreamAsync(_s16, _apiCts.Token).ToBlocking(_enumCts.Token));
}
