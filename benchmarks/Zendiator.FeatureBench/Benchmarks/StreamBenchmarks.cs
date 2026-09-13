using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.FeatureBench;

/// <summary>Streaming dispatch against same-meaning direct IAsyncEnumerable.</summary>
[MemoryDiagnoser]
public class StreamBenchmarks
{
    private readonly ZStream _s0 = new(0);
    private readonly ZStream _s1 = new(1);
    private readonly ZStream _s16 = new(16);
    private readonly ZStream _s1024 = new(1024);

    private IZendiator _zr = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        _zr = scope.ServiceProvider.GetRequiredService<IZendiator>();
    }

    private static async ValueTask<int> Drain(System.Collections.Generic.IAsyncEnumerable<int> stream)
    {
        var sum = 0;
        await foreach (var i in stream) sum += i;
        return sum;
    }

    [Benchmark]
    public ValueTask<int> ZrStream0() => Drain(_zr.StreamAsync(_s0));
    [Benchmark]
    public ValueTask<int> DirectStream0() => Drain(ZrFeatureDirect.DirectStream(_s0, CancellationToken.None));
    [Benchmark]
    public ValueTask<int> ZrStream1() => Drain(_zr.StreamAsync(_s1));
    [Benchmark]
    public ValueTask<int> DirectStream1() => Drain(ZrFeatureDirect.DirectStream(_s1, CancellationToken.None));
    [Benchmark]
    public ValueTask<int> ZrStream16() => Drain(_zr.StreamAsync(_s16));
    [Benchmark]
    public ValueTask<int> DirectStream16() => Drain(ZrFeatureDirect.DirectStream(_s16, CancellationToken.None));
    [Benchmark]
    public ValueTask<int> ZrStream1024() => Drain(_zr.StreamAsync(_s1024));
    [Benchmark]
    public ValueTask<int> DirectStream1024() => Drain(ZrFeatureDirect.DirectStream(_s1024, CancellationToken.None));
}
