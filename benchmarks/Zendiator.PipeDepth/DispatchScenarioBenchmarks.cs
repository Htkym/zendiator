using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.PipeDepth;

/// <summary>Warm dispatch and complete scope lifetimes with observable pipeline work.</summary>
[MemoryDiagnoser]
public class DispatchScenarioBenchmarks
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IZendiator _mediator = null!;
    private CancellationTokenSource _cancellation = null!;
    private int _input = 41;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _mediator = _scope.ServiceProvider.GetRequiredService<IZendiator>();
        _cancellation = new CancellationTokenSource();
        if (_mediator.SendAsync(new O0(_input)).GetAwaiter().GetResult() != 41
            || _mediator.SendAsync(new P5(_input)).GetAwaiter().GetResult() != 42
            || _mediator.SendAsync(new O5(_input), _cancellation.Token).GetAwaiter().GetResult() != 591)
            throw new InvalidOperationException("Dispatch result mismatch.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cancellation.Dispose();
        _scope.Dispose();
        _provider.Dispose();
    }

    [Benchmark]
    public ValueTask<int> WarmNoPipeline() => _mediator.SendAsync(new O0(_input));

    [Benchmark]
    public ValueTask<int> WarmPassThrough5() => _mediator.SendAsync(new P5(_input));

    [Benchmark]
    public ValueTask<int> WarmTransform5() => _mediator.SendAsync(new O5(_input));

    [Benchmark]
    public ValueTask<int> WarmCancelableTransform5() => _mediator.SendAsync(new O5(_input), _cancellation.Token);

    [Benchmark]
    public int ScopeTransform5()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        return mediator.SendAsync(new O5(_input), _cancellation.Token).GetAwaiter().GetResult();
    }

    [Benchmark]
    public int ScopeTransform5Batch16()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 16; i++)
            sum += mediator.SendAsync(new O5(_input + i), _cancellation.Token).GetAwaiter().GetResult();
        return sum;
    }
}
