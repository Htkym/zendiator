using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Zendiator.Benchmarks;

/// <summary>DI boundary separation: A1/B1/B2/B3/D0/D1 measurement modes.</summary>
[MemoryDiagnoser]
public class DiBenchmarks
{
    private ServiceProvider _zrScopedProvider = null!;
    private ServiceProvider _zrSingletonProvider = null!;
    private ServiceProvider _mrProvider = null!;
    private ServiceProvider _moProvider = null!;
    private IServiceScope _zrB1Scope = null!;
    private IServiceScope _mrB1Scope = null!;
    private IServiceScope _moB1Scope = null!;
    private Zp0 _zp0 = new(41);
    private Zp5 _zp5 = new(41);
    private MrP0 _mrP0 = new(41);
    private MoP0 _moP0 = new(41);

    [GlobalSetup]
    public void Setup()
    {
        _zrScopedProvider = ZrHost.CreateScoped();
        _zrSingletonProvider = ZrHost.CreateSingleton();
        _mrProvider = MrHost.CreateProvider();
        _moProvider = MoHost.CreateProvider();
        _zrB1Scope = _zrScopedProvider.CreateScope();
        _mrB1Scope = _mrProvider.CreateScope();
        _moB1Scope = _moProvider.CreateScope();
        _ = _zrB1Scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(_zp0).GetAwaiter().GetResult();
        _ = _zrB1Scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(_zp5).GetAwaiter().GetResult();
        _ = _zrSingletonProvider.GetRequiredService<IZendiator>().SendAsync(_zp0).GetAwaiter().GetResult();
        _ = _mrB1Scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>().Send(_mrP0).GetAwaiter().GetResult();
        _ = _moB1Scope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>().Send(_moP0).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _zrB1Scope.Dispose();
        _mrB1Scope.Dispose();
        _moB1Scope.Dispose();
        _zrScopedProvider.Dispose();
        _zrSingletonProvider.Dispose();
        _mrProvider.Dispose();
        _moProvider.Dispose();
    }

    [Benchmark(Description = "Zr Scoped B2 K=1")]
    public int ZrScopedFullPath()
    {
        using var scope = _zrScopedProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(_zp0).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Zr Scoped B2 K=4")]
    public int ZrScopedB2K4()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        sum += mediator.SendAsync(_zp0).GetAwaiter().GetResult();
        sum += mediator.SendAsync(_zp0).GetAwaiter().GetResult();
        sum += mediator.SendAsync(_zp0).GetAwaiter().GetResult();
        sum += mediator.SendAsync(_zp0).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped B2 K=16")]
    public int ZrScopedB2K16()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 16; i++)
            sum += mediator.SendAsync(_zp0).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped B2 K=2")]
    public int ZrScopedB2K2()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 2; i++)
            sum += mediator.SendAsync(_zp0).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped B2 K=3")]
    public int ZrScopedB2K3()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 3; i++)
            sum += mediator.SendAsync(_zp0).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped B2 K=8")]
    public int ZrScopedB2K8()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 8; i++)
            sum += mediator.SendAsync(_zp0).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped 5 B2 K=1")]
    public int ZrScoped5B2K1()
    {
        using var scope = _zrScopedProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(_zp5).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Zr Scoped 5 B2 K=2")]
    public int ZrScoped5B2K2()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 2; i++)
            sum += mediator.SendAsync(_zp5).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped 5 B2 K=3")]
    public int ZrScoped5B2K3()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 3; i++)
            sum += mediator.SendAsync(_zp5).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped 5 B2 K=4")]
    public int ZrScoped5B2K4()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 4; i++)
            sum += mediator.SendAsync(_zp5).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped 5 B2 K=8")]
    public int ZrScoped5B2K8()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 8; i++)
            sum += mediator.SendAsync(_zp5).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Scoped 5 B2 K=16")]
    public int ZrScoped5B2K16()
    {
        using var scope = _zrScopedProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var sum = 0;
        for (var i = 0; i < 16; i++)
            sum += mediator.SendAsync(_zp5).GetAwaiter().GetResult();
        return sum;
    }

    [Benchmark(Description = "Zr Singleton B3")]
    public int ZrSingletonFullPath()
    {
        return _zrSingletonProvider.GetRequiredService<IZendiator>().SendAsync(_zp0).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "MediatR B2 K=1")]
    public int MediatRFullPath()
    {
        using var scope = _mrProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>().Send(_mrP0).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Mo B2 K=1")]
    public int MoFullPath()
    {
        using var scope = _moProvider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>().Send(_moP0).GetAwaiter().GetResult();
    }

    [Benchmark(Description = "Zr Scoped B1")]
    public int ZrScopedB1() => _zrB1Scope.ServiceProvider.GetRequiredService<IZendiator>().SendAsync(_zp0).GetAwaiter().GetResult();

    [Benchmark(Description = "MediatR B1")]
    public int MediatRB1() => _mrB1Scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>().Send(_mrP0).GetAwaiter().GetResult();

    [Benchmark(Description = "Mo B1")]
    public int MoB1() => _moB1Scope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>().Send(_moP0).GetAwaiter().GetResult();

    [Benchmark(Description = "Empty scope D0")]
    public void EmptyScopeD0()
    {
        using var scope = _zrScopedProvider.CreateScope();
    }

    [Benchmark(Description = "Zr Scoped D1")]
    public IZendiator ZrScopedD1() => _zrB1Scope.ServiceProvider.GetRequiredService<IZendiator>();

    [Benchmark(Description = "Zr Singleton D1")]
    public IZendiator ZrSingletonD1() => _zrSingletonProvider.GetRequiredService<IZendiator>();
}
