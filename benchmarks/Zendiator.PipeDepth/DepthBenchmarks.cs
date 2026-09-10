using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.PipeDepth;

// Depth diagnostics and observable routes (O1(41)=151, O5(41)=591).
[MemoryDiagnoser]
public class DepthBenchmarks
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IZendiator _mediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _mediator = _scope.ServiceProvider.GetRequiredService<IZendiator>();
        _ = _mediator.SendAsync(new P8(41)).GetAwaiter().GetResult();
        _ = _mediator.SendAsync(new O5(41)).GetAwaiter().GetResult();
        _ = _mediator.SendAsync(new Oz5(41)).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Benchmark(Description = "Depth P2")]
    public ValueTask<int> P2() => _mediator.SendAsync(new P2(41));

    [Benchmark(Description = "Depth P3")]
    public ValueTask<int> P3() => _mediator.SendAsync(new P3(41));

    [Benchmark(Description = "Depth P4")]
    public ValueTask<int> P4() => _mediator.SendAsync(new P4(41));

    [Benchmark(Description = "Depth P5")]
    public ValueTask<int> P5() => _mediator.SendAsync(new P5(41));

    [Benchmark(Description = "Depth P6")]
    public ValueTask<int> P6() => _mediator.SendAsync(new P6(41));

    [Benchmark(Description = "Depth P8")]
    public ValueTask<int> P8() => _mediator.SendAsync(new P8(41));

    [Benchmark(Description = "Obs O0")]
    public ValueTask<int> O0() => _mediator.SendAsync(new O0(41));

    [Benchmark(Description = "Obs O1")]
    public ValueTask<int> O1() => _mediator.SendAsync(new O1(41));

    [Benchmark(Description = "Obs O3")]
    public ValueTask<int> O3() => _mediator.SendAsync(new O3(41));

    [Benchmark(Description = "Obs O5")]
    public ValueTask<int> O5() => _mediator.SendAsync(new O5(41));

    [Benchmark(Description = "Oz O0")]
    public ValueTask<int> Oz0() => _mediator.SendAsync(new Oz0(41));

    [Benchmark(Description = "Oz O1")]
    public ValueTask<int> Oz1() => _mediator.SendAsync(new Oz1(41));

    [Benchmark(Description = "Oz O3")]
    public ValueTask<int> Oz3() => _mediator.SendAsync(new Oz3(41));

    [Benchmark(Description = "Oz O5")]
    public ValueTask<int> Oz5() => _mediator.SendAsync(new Oz5(41));
}
