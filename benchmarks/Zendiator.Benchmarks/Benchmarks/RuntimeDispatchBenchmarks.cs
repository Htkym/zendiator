using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

/// <summary>Runtime-type (object) dispatch where supported.
/// This library generates concrete-type overloads only, so it is N/A by design.</summary>
[MemoryDiagnoser]
public class RuntimeDispatchBenchmarks
{
    private MrP0 _mrPing = new(41);
    private MoP0 _moPing = new(41);
    private DsPing _dsPing = new(41);

    private global::MediatR.ISender _mr = null!;
    private global::Mediator.IMediator _mo = null!;
    private global::DSoftStudio.Mediator.Abstractions.ISender _ds = null!;

    [GlobalSetup]
    public void Setup()
    {
        var mr = MrHost.CreateProvider();
        _mr = mr.CreateScope().ServiceProvider.GetRequiredService<global::MediatR.ISender>();
        var mo = MoHost.CreateProvider();
        _mo = mo.CreateScope().ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        var ds = DsHost.CreateProvider();
        _ds = ds.CreateScope().ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.ISender>();

        // Warmup outside measurement.
        _ = _mr.Send((object)_mrPing).GetAwaiter().GetResult();
        _ = _mo.Send((object)_moPing).GetAwaiter().GetResult();
        _ = _ds.Send((object)_dsPing).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "MediatR object")]
    public Task<object?> MediatRObject() => _mr.Send((object)_mrPing);

    [Benchmark(Description = "Mo object")]
    public ValueTask<object?> MoObject() => _mo.Send((object)_moPing);

    [Benchmark(Description = "DSoft object")]
    public ValueTask<object?> DsObject() => _ds.Send((object)_dsPing);
}
