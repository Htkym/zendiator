using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

/// <summary>Single-request dispatch. Every handler computes Value + 1.</summary>
[MemoryDiagnoser]
public class RequestBenchmarks
{
    private Zp0 _zp0 = new(41);
    private MrP0 _mrP0 = new(41);
    private MoP0 _moP0 = new(41);
    private DrP0 _drP0 = new(41);
    private DsP0 _dsP0 = new(41);
    private FoPing _foPing = new(41);
    private IhPing.Query _ihPing = new(41);

    private IZendiator _zrScoped = null!;
    private Zendiator _zrScopedConcrete = null!;
    private IZendiator _zrSingleton = null!;
    private global::MediatR.IMediator _mr = null!;
    private global::Mediator.IMediator _mo = null!;
    private global::Mediator.Mediator _moConcrete = null!;
    private global::DispatchR.IMediator _dr = null!;
    private global::DSoftStudio.Mediator.Abstractions.IMediator _ds = null!;
    private global::Foundatio.Mediator.IMediator _fo = null!;
    private IhPing.Handler _ih = null!;

    [GlobalSetup]
    public void Setup()
    {
        var zr = ZrHost.CreateScoped();
        var zrScope = zr.CreateScope();
        _zrScoped = zrScope.ServiceProvider.GetRequiredService<IZendiator>();
        _zrScopedConcrete = zrScope.ServiceProvider.GetRequiredService<Zendiator>();

        var zrSingle = ZrHost.CreateSingleton();
        _zrSingleton = zrSingle.GetRequiredService<IZendiator>();

        var mr = MrHost.CreateProvider();
        var mrScope = mr.CreateScope();
        _mr = mrScope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>();

        var mo = MoHost.CreateProvider();
        var moScope = mo.CreateScope();
        _mo = moScope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        _moConcrete = moScope.ServiceProvider.GetRequiredService<global::Mediator.Mediator>();

        var dr = DrHost.CreateProvider();
        var drScope = dr.CreateScope();
        _dr = drScope.ServiceProvider.GetRequiredService<global::DispatchR.IMediator>();

        var ds = DsHost.CreateProvider();
        var dsScope = ds.CreateScope();
        _ds = dsScope.ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();

        var fo = FoHost.CreateProvider();
        var foScope = fo.CreateScope();
        _fo = foScope.ServiceProvider.GetRequiredService<global::Foundatio.Mediator.IMediator>();

        var ih = IhHost.CreateProvider();
        var ihScope = ih.CreateScope();
        _ih = ihScope.ServiceProvider.GetRequiredService<IhPing.Handler>();

        // Warmup outside measurement.
        _ = _zrScoped.SendAsync(_zp0).GetAwaiter().GetResult();
        _ = _zrScopedConcrete.SendAsync(_zp0).GetAwaiter().GetResult();
        _ = _zrSingleton.SendAsync(_zp0).GetAwaiter().GetResult();
        _ = _mr.Send(_mrP0).GetAwaiter().GetResult();
        _ = _mo.Send(_moP0).GetAwaiter().GetResult();
        _ = _moConcrete.Send(_moP0).GetAwaiter().GetResult();
        _ = _dr.Send<DrP0, ValueTask<int>>(_drP0, CancellationToken.None).GetAwaiter().GetResult();
        _ = _ds.Send(_dsP0).GetAwaiter().GetResult();
        _ = _fo.Invoke<int>(_foPing);
        _ = _ih.HandleAsync(_ihPing).GetAwaiter().GetResult();
    }

    [Benchmark(Baseline = true, Description = "Direct sync")]
    public int DirectSync() => _zp0.Value + 1;

    [Benchmark(Description = "Direct ValueTask")]
    public ValueTask<int> DirectValueTask() => new(_zp0.Value + 1);

    [Benchmark(Description = "Direct Task")]
    public Task<int> DirectTask() => Task.FromResult(_zp0.Value + 1);

    [Benchmark(Description = "Zendiator Scoped")]
    public ValueTask<int> ZrScoped() => _zrScoped.SendAsync(_zp0);

    [Benchmark(Description = "Zendiator Scoped concrete")]
    public ValueTask<int> ZrScopedConcrete() => _zrScopedConcrete.SendAsync(_zp0);

    [Benchmark(Description = "Zendiator Singleton")]
    public ValueTask<int> ZrSingleton() => _zrSingleton.SendAsync(_zp0);

    [Benchmark(Description = "MediatR")]
    public Task<int> MediatR() => _mr.Send(_mrP0);

    [Benchmark(Description = "MoMediator interface")]
    public ValueTask<int> MoInterface() => _mo.Send(_moP0);

    [Benchmark(Description = "MoMediator concrete")]
    public ValueTask<int> MoConcrete() => _moConcrete.Send(_moP0);

    [Benchmark(Description = "DispatchR")]
    public ValueTask<int> DispatchR() => _dr.Send<DrP0, ValueTask<int>>(_drP0, CancellationToken.None);

    [Benchmark(Description = "DSoft")]
    public ValueTask<int> DSoft() => _ds.Send(_dsP0);

    [Benchmark(Description = "Foundatio sync")]
    public int FoundatioSync() => _fo.Invoke<int>(_foPing);

    [Benchmark(Description = "Immediate.Handlers")]
    public ValueTask<int> ImmediateHandlers() => _ih.HandleAsync(_ihPing);
}
