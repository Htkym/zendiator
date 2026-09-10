using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

// Same-meaning observable lanes: echo handler, +100 per layer downstream.
[MemoryDiagnoser]
public class ObservableBenchmarks
{
    private MoO0 _moO0 = new(41);
    private MoO1 _moO1 = new(41);
    private MoO3 _moO3 = new(41);
    private MoO5 _moO5 = new(41);
    private DrO0 _drO0 = new(41);
    private DrO1 _drO1 = new(41);
    private DrO3 _drO3 = new(41);
    private DrO5 _drO5 = new(41);
    private global::Zendiator.DSoftObs.DsO0 _dsO0 = new(41);
    private IhO0.Query _ihO0 = new(41);
    private IhO1.Query _ihO1 = new(41);
    private IhO3.Query _ihO3 = new(41);
    private IhO5.Query _ihO5 = new(41);

    private global::Mediator.IMediator _mo = null!;
    private global::DispatchR.IMediator _dr = null!;
    private global::DSoftStudio.Mediator.Abstractions.IMediator _ds = null!;
    private global::DSoftStudio.Mediator.Abstractions.IMediator _dsObs = null!;
    private IhO0.Handler _ihO0h = null!;
    private IhO1.Handler _ihO1h = null!;
    private IhO3.Handler _ihO3h = null!;
    private IhO5.Handler _ihO5h = null!;

    [GlobalSetup]
    public void Setup()
    {
        var mo = MoHost.CreateProvider();
        _mo = mo.CreateScope().ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        var dr = DrHost.CreateProvider();
        _dr = dr.CreateScope().ServiceProvider.GetRequiredService<global::DispatchR.IMediator>();
        var ds = DsHost.CreateProvider();
        _ds = ds.CreateScope().ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();
        var dsObs = global::Zendiator.DSoftObs.DsObsHost.CreateProvider();
        _dsObs = dsObs.CreateScope().ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();
        var ih = IhHost.CreateProvider();
        var ihScope = ih.CreateScope();
        _ihO0h = ihScope.ServiceProvider.GetRequiredService<IhO0.Handler>();
        _ihO1h = ihScope.ServiceProvider.GetRequiredService<IhO1.Handler>();
        _ihO3h = ihScope.ServiceProvider.GetRequiredService<IhO3.Handler>();
        _ihO5h = ihScope.ServiceProvider.GetRequiredService<IhO5.Handler>();

        _ = _mo.Send(_moO5).GetAwaiter().GetResult();
        _ = _dr.Send<DrO5, ValueTask<int>>(_drO5, CancellationToken.None).GetAwaiter().GetResult();
        _ = _ds.Send(new DsOz0(41)).GetAwaiter().GetResult();
        _ = _dsObs.Send(_dsO0).GetAwaiter().GetResult();
        _ = _ihO5h.HandleAsync(_ihO5).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Obs Mo 0")]
    public ValueTask<int> MoO0() => _mo.Send(_moO0);

    [Benchmark(Description = "Obs Mo 1")]
    public ValueTask<int> MoO1() => _mo.Send(_moO1);

    [Benchmark(Description = "Obs Mo 3")]
    public ValueTask<int> MoO3() => _mo.Send(_moO3);

    [Benchmark(Description = "Obs Mo 5")]
    public ValueTask<int> MoO5() => _mo.Send(_moO5);

    [Benchmark(Description = "Obs DispatchR 0")]
    public ValueTask<int> DrO0() => _dr.Send<DrO0, ValueTask<int>>(_drO0, CancellationToken.None);

    [Benchmark(Description = "Obs DispatchR 1")]
    public ValueTask<int> DrO1() => _dr.Send<DrO1, ValueTask<int>>(_drO1, CancellationToken.None);

    [Benchmark(Description = "Obs DispatchR 3")]
    public ValueTask<int> DrO3() => _dr.Send<DrO3, ValueTask<int>>(_drO3, CancellationToken.None);

    [Benchmark(Description = "Obs DispatchR 5")]
    public ValueTask<int> DrO5() => _dr.Send<DrO5, ValueTask<int>>(_drO5, CancellationToken.None);

    [Benchmark(Description = "Obs DSoft 0")]
    public ValueTask<int> DsO0() => _ds.Send(new DsOz0(41));

    [Benchmark(Description = "Obs DSoft 1")]
    public ValueTask<int> DsObs1() => _dsObs.Send(_dsO0);

    [Benchmark(Description = "Obs IH 0")]
    public ValueTask<int> IhO0() => _ihO0h.HandleAsync(_ihO0);

    [Benchmark(Description = "Obs IH 1")]
    public ValueTask<int> IhO1() => _ihO1h.HandleAsync(_ihO1);

    [Benchmark(Description = "Obs IH 3")]
    public ValueTask<int> IhO3() => _ihO3h.HandleAsync(_ihO3);

    [Benchmark(Description = "Obs IH 5")]
    public ValueTask<int> IhO5() => _ihO5h.HandleAsync(_ihO5);
}
