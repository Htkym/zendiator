using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.FeatureBench;

/// <summary>FeatureExpansion-1 dispatch paths against same-meaning direct calls.</summary>
[MemoryDiagnoser]
public class FeatureBenchmarks
{
    private readonly ZVoid _void = new(41);
    private readonly ZEv1 _ev1 = new(41);
    private readonly ZEv4 _ev4 = new(41);
    private readonly ZMulti _multi = new(41);
    private readonly ZGen<ZDto> _gen = new(41);
    private readonly byte[] _bytes = new byte[32];

    private IZendiator _zr = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        _zr = scope.ServiceProvider.GetRequiredService<IZendiator>();
        _zr.SendAsync(_void).GetAwaiter().GetResult();
        _zr.PublishAsync(_ev1).GetAwaiter().GetResult();
        _zr.PublishAsync(_ev4).GetAwaiter().GetResult();
        _ = _zr.SendAllAsync(_multi).GetAwaiter().GetResult();
        _ = _zr.SendSync(new ZSpan(_bytes));
        _ = _zr.SendAsync(_gen).GetAwaiter().GetResult();
    }

    [Benchmark]
    public ValueTask ZrVoid() => _zr.SendAsync(_void);

    [Benchmark]
    public ValueTask DirectVoid() => ZrFeatureDirect.DirectVoid(_void, CancellationToken.None);

    [Benchmark]
    public ValueTask ZrNotify1() => _zr.PublishAsync(_ev1);

    [Benchmark]
    public ValueTask DirectNotify1() => ZrFeatureDirect.DirectFanout1(_ev1, CancellationToken.None);

    [Benchmark]
    public ValueTask ZrNotify4() => _zr.PublishAsync(_ev4);

    [Benchmark]
    public ValueTask DirectNotify4() => ZrFeatureDirect.DirectFanout4(_ev4, CancellationToken.None);

    [Benchmark]
    public ValueTask<System.Collections.Generic.IReadOnlyList<int>> ZrMulti2() => _zr.SendAllAsync(_multi);

    [Benchmark]
    public ValueTask<System.Collections.Generic.IReadOnlyList<int>> DirectMulti2() => ZrFeatureDirect.DirectMulti(_multi, CancellationToken.None);

    [Benchmark]
    public int ZrSyncSpan() => _zr.SendSync(new ZSpan(_bytes));

    [Benchmark]
    public int DirectSyncSpan() => ZrFeatureDirect.DirectSpan(new ZSpan(_bytes), CancellationToken.None);

    [Benchmark]
    public ValueTask<ZDto> ZrGeneric() => _zr.SendAsync(_gen);

    [Benchmark]
    public ValueTask<ZDto> DirectGeneric() => ZrFeatureDirect.DirectGen(_gen, CancellationToken.None);
}
