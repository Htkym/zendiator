using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.PipeDepth;

// Depth disassembly diagnostics only; timing rows are never ranked.
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class DepthDisassemblyBenchmarks
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
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Benchmark(Description = "Disasm P4")]
    public ValueTask<int> DisasmP4() => _mediator.SendAsync(new P4(41));

    [Benchmark(Description = "Disasm P5")]
    public ValueTask<int> DisasmP5() => _mediator.SendAsync(new P5(41));

    [Benchmark(Description = "Disasm P6")]
    public ValueTask<int> DisasmP6() => _mediator.SendAsync(new P6(41));

    [Benchmark(Description = "Disasm P8")]
    public ValueTask<int> DisasmP8() => _mediator.SendAsync(new P8(41));
}
