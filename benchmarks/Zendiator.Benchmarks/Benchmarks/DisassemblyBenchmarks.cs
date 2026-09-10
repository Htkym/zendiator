using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

// Disassembly diagnostics only; timing rows are never ranked.
[MemoryDiagnoser]
[DisassemblyDiagnoser(maxDepth: 3)]
public class DisassemblyBenchmarks
{
    private Zp0 _zp0 = new(41);
    private Zp5 _zp5 = new(41);
    private MoP5 _moP5 = new(41);
    private int _directValue = 41;

    private IZendiator _zrScoped = null!;
    private IZendiator _zrSingleton = null!;
    private global::Mediator.IMediator _mo = null!;
    private ServiceProvider _zrProvider = null!;
    private IServiceScope _zrScope = null!;
    private ServiceProvider _moProvider = null!;
    private IServiceScope _moScope = null!;

    [GlobalSetup]
    public void Setup()
    {
        _zrProvider = ZrHost.CreateScoped();
        _zrScope = _zrProvider.CreateScope();
        _zrScoped = _zrScope.ServiceProvider.GetRequiredService<IZendiator>();
        var zrSingle = ZrHost.CreateSingleton();
        _zrSingleton = zrSingle.GetRequiredService<IZendiator>();
        _moProvider = MoHost.CreateProvider();
        _moScope = _moProvider.CreateScope();
        _mo = _moScope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>();

        _ = _zrScoped.SendAsync(_zp5).GetAwaiter().GetResult();
        _ = _zrSingleton.SendAsync(_zp5).GetAwaiter().GetResult();
        _ = _mo.Send(_moP5).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _zrScope.Dispose();
        _zrProvider.Dispose();
        _moScope.Dispose();
        _moProvider.Dispose();
    }

    [Benchmark(Description = "Disasm Direct 5")]
    public int DisasmDirect5() => _directValue + 1;

    [Benchmark(Description = "Disasm Zr Scoped 0")]
    public ValueTask<int> DisasmZrScoped0() => _zrScoped.SendAsync(_zp0);

    [Benchmark(Description = "Disasm Zr Scoped 5")]
    public ValueTask<int> DisasmZrScoped5() => _zrScoped.SendAsync(_zp5);

    [Benchmark(Description = "Disasm Zr Singleton 0")]
    public ValueTask<int> DisasmZrSingleton0() => _zrSingleton.SendAsync(_zp0);

    [Benchmark(Description = "Disasm Zr Singleton 5")]
    public ValueTask<int> DisasmZrSingleton5() => _zrSingleton.SendAsync(_zp5);

    [Benchmark(Description = "Disasm Mo 5")]
    public ValueTask<int> DisasmMo5() => _mo.Send(_moP5);
}
