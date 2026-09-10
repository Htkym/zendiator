using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Zendiator.DiBench.Generated;

namespace Zendiator.DiBench;

/// <summary>DI-configured dispatch paths (B01/B03/B05/B06). Registration cost is one-time per call.</summary>
[MemoryDiagnoser]
public class DiBenchmarks
{
    private readonly Warm _warm = new(41);
    private readonly Wipe _wipe = new(41);
    private readonly Ticked _ticked = new(41);
    private readonly Duo _duo = new(41);
    private readonly byte[] _bytes = new byte[32];

    private IZendiator _zr = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddBench();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        _zr = scope.ServiceProvider.GetRequiredService<IZendiator>();
        _ = _zr.SendAsync(_warm).GetAwaiter().GetResult();
        _zr.SendAsync(_wipe).GetAwaiter().GetResult();
        _zr.PublishAsync(_ticked).GetAwaiter().GetResult();
        _ = _zr.SendAllAsync(_duo).GetAwaiter().GetResult();
        _ = _zr.SendSync(new SpanSum(_bytes));
    }

    [Benchmark]
    public ValueTask<int> DiSend() => _zr.SendAsync(_warm);

    [Benchmark]
    public ValueTask DiVoid() => _zr.SendAsync(_wipe);

    [Benchmark]
    public ValueTask DiNotify2() => _zr.PublishAsync(_ticked);

    [Benchmark]
    public ValueTask<System.Collections.Generic.IReadOnlyList<int>> DiMulti2() => _zr.SendAllAsync(_duo);

    [Benchmark]
    public int DiSyncSpan() => _zr.SendSync(new SpanSum(_bytes));
}
