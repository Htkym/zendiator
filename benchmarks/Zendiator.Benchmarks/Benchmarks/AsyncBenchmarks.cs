using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

/// <summary>Async dispatch crossing a real async boundary (Task.Yield).
/// Absolute numbers are yield-dominated; only cross-library parity matters.</summary>
[MemoryDiagnoser]
public class AsyncBenchmarks
{
    private ZpAsync _zr = new(41);
    private MrAsyncPing _mr = new(41);
    private MoAsyncPing _mo = new(41);

    private IZendiator _zrMediator = null!;
    private global::MediatR.IMediator _mrMediator = null!;
    private global::Mediator.IMediator _moMediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var zr = ZrHost.CreateSingleton();
        _zrMediator = zr.GetRequiredService<IZendiator>();
        var mr = MrHost.CreateProvider();
        _mrMediator = mr.CreateScope().ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        var mo = MoHost.CreateProvider();
        _moMediator = mo.CreateScope().ServiceProvider.GetRequiredService<global::Mediator.IMediator>();

        _ = _zrMediator.SendAsync(_zr).GetAwaiter().GetResult();
        _ = _mrMediator.Send(_mr).GetAwaiter().GetResult();
        _ = _moMediator.Send(_mo).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Zendiator async")]
    public ValueTask<int> ZrAsync() => _zrMediator.SendAsync(_zr);

    [Benchmark(Description = "MediatR async")]
    public Task<int> MediatRAsync() => _mrMediator.Send(_mr);

    [Benchmark(Description = "Mo async")]
    public ValueTask<int> MoAsync() => _moMediator.Send(_mo);
}
