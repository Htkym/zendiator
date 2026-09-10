using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

/// <summary>Notification fan-out. This library has no publish API by design (N/A).</summary>
[MemoryDiagnoser]
public class NotificationBenchmarks
{
    private MrEv1 _mrEv1 = new(41);
    private MrEv4 _mrEv4 = new(41);
    private MrEv16 _mrEv16 = new(41);
    private MoEv1 _moEv1 = new(41);
    private MoEv4 _moEv4 = new(41);
    private MoEv16 _moEv16 = new(41);
    private DrEv1 _drEv1 = new(41);
    private DrEv4 _drEv4 = new(41);

    private global::MediatR.IMediator _mr = null!;
    private global::Mediator.IMediator _mo = null!;
    private global::DispatchR.IMediator _dr = null!;

    [GlobalSetup]
    public void Setup()
    {
        var mr = MrHost.CreateProvider();
        _mr = mr.CreateScope().ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        var mo = MoHost.CreateProvider();
        _mo = mo.CreateScope().ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        var dr = DrHost.CreateProvider();
        _dr = dr.CreateScope().ServiceProvider.GetRequiredService<global::DispatchR.IMediator>();

        // Warmup outside measurement.
        _mr.Publish(_mrEv16).GetAwaiter().GetResult();
        _mo.Publish(_moEv16).GetAwaiter().GetResult();
        _dr.Publish(_drEv4, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "MediatR 1")]
    public Task MediatR1() => _mr.Publish(_mrEv1);

    [Benchmark(Description = "MediatR 4")]
    public Task MediatR4() => _mr.Publish(_mrEv4);

    [Benchmark(Description = "MediatR 16")]
    public Task MediatR16() => _mr.Publish(_mrEv16);

    [Benchmark(Description = "Mo 1")]
    public ValueTask Mo1() => _mo.Publish(_moEv1);

    [Benchmark(Description = "Mo 4")]
    public ValueTask Mo4() => _mo.Publish(_moEv4);

    [Benchmark(Description = "Mo 16")]
    public ValueTask Mo16() => _mo.Publish(_moEv16);

    [Benchmark(Description = "DispatchR 1")]
    public ValueTask Dr1() => _dr.Publish(_drEv1, CancellationToken.None);

    [Benchmark(Description = "DispatchR 4")]
    public ValueTask Dr4() => _dr.Publish(_drEv4, CancellationToken.None);
}
