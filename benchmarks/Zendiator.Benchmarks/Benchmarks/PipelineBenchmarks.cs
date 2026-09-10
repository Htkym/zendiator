using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;

namespace Zendiator.Benchmarks;

/// <summary>Pipeline infrastructure cost at 0/1/3/5 minimal pass-through behaviors.</summary>
[MemoryDiagnoser]
public class PipelineBenchmarks
{
    private Zp0 _zp0 = new(41);
    private Zp1 _zp1 = new(41);
    private Zp3 _zp3 = new(41);
    private Zp5 _zp5 = new(41);
    private MrP0 _mrP0 = new(41);
    private MrP1 _mrP1 = new(41);
    private MrP3 _mrP3 = new(41);
    private MrP5 _mrP5 = new(41);
    private MoP0 _moP0 = new(41);
    private MoP1 _moP1 = new(41);
    private MoP3 _moP3 = new(41);
    private MoP5 _moP5 = new(41);
    private DrP0 _drP0 = new(41);
    private DrP1 _drP1 = new(41);
    private DrP3 _drP3 = new(41);
    private DrP5 _drP5 = new(41);
    private DsP0 _dsP0 = new(41);
    private IhP0.Query _ihP0 = new(41);
    private IhP1.Query _ihP1 = new(41);
    private IhP3.Query _ihP3 = new(41);
    private IhP5.Query _ihP5 = new(41);

    private IZendiator _zrScoped = null!;
    private IZendiator _zrSingleton = null!;
    private global::MediatR.IMediator _mr = null!;
    private global::Mediator.IMediator _mo = null!;
    private global::DispatchR.IMediator _dr = null!;
    private global::DSoftStudio.Mediator.Abstractions.IMediator _ds = null!;
    private IhP0.Handler _ih0 = null!;
    private IhP1.Handler _ih1 = null!;
    private IhP3.Handler _ih3 = null!;
    private IhP5.Handler _ih5 = null!;
    private int _directValue = 41;

    // Hand-written reference chain over one shared instance set; DI lookup absent by construction.
    private ServiceProvider _e02Provider = null!;
    private IServiceScope _e02Scope = null!;
    private Zp0Handler _eH0 = null!;
    private Zb1<Zp1, int> _eB1 = null!;
    private Zp1Handler _eH1 = null!;
    private Zb1<Zp3, int> _e31 = null!;
    private Zb2<Zp3, int> _e32 = null!;
    private Zb3<Zp3, int> _e33 = null!;
    private Zp3Handler _eH3 = null!;
    private Zb1<Zp5, int> _e51 = null!;
    private Zb2<Zp5, int> _e52 = null!;
    private Zb3<Zp5, int> _e53 = null!;
    private Zb4<Zp5, int> _e54 = null!;
    private Zb5<Zp5, int> _e55 = null!;
    private Zp5Handler _eH5 = null!;

    [GlobalSetup]
    public void Setup()
    {
        var zr = ZrHost.CreateScoped();
        _zrScoped = zr.CreateScope().ServiceProvider.GetRequiredService<IZendiator>();
        var zrSingle = ZrHost.CreateSingleton();
        _zrSingleton = zrSingle.GetRequiredService<IZendiator>();
        var mr = MrHost.CreateProvider();
        _mr = mr.CreateScope().ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        var mo = MoHost.CreateProvider();
        _mo = mo.CreateScope().ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        var dr = DrHost.CreateProvider();
        _dr = dr.CreateScope().ServiceProvider.GetRequiredService<global::DispatchR.IMediator>();
        var ds = DsHost.CreateProvider();
        _ds = ds.CreateScope().ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();
        var ih = IhHost.CreateProvider();
        var ihScope = ih.CreateScope();
        _ih0 = ihScope.ServiceProvider.GetRequiredService<IhP0.Handler>();
        _ih1 = ihScope.ServiceProvider.GetRequiredService<IhP1.Handler>();
        _ih3 = ihScope.ServiceProvider.GetRequiredService<IhP3.Handler>();
        _ih5 = ihScope.ServiceProvider.GetRequiredService<IhP5.Handler>();

        _ = _zrScoped.SendAsync(_zp5).GetAwaiter().GetResult();
        _ = _zrSingleton.SendAsync(_zp5).GetAwaiter().GetResult();
        _ = _zrSingleton.SendAsync(_zp3).GetAwaiter().GetResult();
        _ = _zrScoped.SendAsync(_zp0).GetAwaiter().GetResult();
        _ = _zrSingleton.SendAsync(_zp0).GetAwaiter().GetResult();
        _ = _mr.Send(_mrP5).GetAwaiter().GetResult();
        _ = _mo.Send(_moP5).GetAwaiter().GetResult();
        _ = _dr.Send<DrP5, ValueTask<int>>(_drP5, CancellationToken.None).GetAwaiter().GetResult();
        _ = _ds.Send(_dsP0).GetAwaiter().GetResult();
        _ = _ih5.HandleAsync(_ihP5).GetAwaiter().GetResult();

        _e02Provider = ZrHost.CreateScoped();
        _e02Scope = _e02Provider.CreateScope();
        var esp = _e02Scope.ServiceProvider;
        _eH0 = esp.GetRequiredService<Zp0Handler>();
        _eB1 = esp.GetRequiredService<Zb1<Zp1, int>>();
        _eH1 = esp.GetRequiredService<Zp1Handler>();
        _e31 = esp.GetRequiredService<Zb1<Zp3, int>>();
        _e32 = esp.GetRequiredService<Zb2<Zp3, int>>();
        _e33 = esp.GetRequiredService<Zb3<Zp3, int>>();
        _eH3 = esp.GetRequiredService<Zp3Handler>();
        _e51 = esp.GetRequiredService<Zb1<Zp5, int>>();
        _e52 = esp.GetRequiredService<Zb2<Zp5, int>>();
        _e53 = esp.GetRequiredService<Zb3<Zp5, int>>();
        _e54 = esp.GetRequiredService<Zb4<Zp5, int>>();
        _e55 = esp.GetRequiredService<Zb5<Zp5, int>>();
        _eH5 = esp.GetRequiredService<Zp5Handler>();
        _ = E02Direct5().GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _e02Scope.Dispose();
        _e02Provider.Dispose();
    }

    [Benchmark(Description = "Zr Scoped 0")]
    public ValueTask<int> ZrScoped0() => _zrScoped.SendAsync(_zp0);

    [Benchmark(Description = "Zr Scoped 1")]
    public ValueTask<int> ZrScoped1() => _zrScoped.SendAsync(_zp1);

    [Benchmark(Description = "Zr Scoped 3")]
    public ValueTask<int> ZrScoped3() => _zrScoped.SendAsync(_zp3);

    [Benchmark(Description = "Zr Scoped 5")]
    public ValueTask<int> ZrScoped5() => _zrScoped.SendAsync(_zp5);

    [Benchmark(Description = "Zr Singleton 0")]
    public ValueTask<int> ZrSingleton0() => _zrSingleton.SendAsync(_zp0);

    [Benchmark(Description = "Zr Singleton 1")]
    public ValueTask<int> ZrSingleton1() => _zrSingleton.SendAsync(_zp1);

    [Benchmark(Description = "Zr Singleton 3")]
    public ValueTask<int> ZrSingleton3() => _zrSingleton.SendAsync(_zp3);

    [Benchmark(Description = "Zr Singleton 5")]
    public ValueTask<int> ZrSingleton5() => _zrSingleton.SendAsync(_zp5);

    // Inlinable pass-through floor. Never ratio against near-zero values.
    [Benchmark(Description = "Direct 0")]
    public int Direct0() => _directValue + 1;

    [Benchmark(Description = "Direct 1")]
    public int Direct1() => Pass1(_directValue);

    [Benchmark(Description = "Direct 3")]
    public int Direct3() => Pass3(Pass2(Pass1(_directValue)));

    [Benchmark(Description = "Direct 5")]
    public int Direct5() => Pass5(Pass4(Pass3(Pass2(Pass1(_directValue)))));

    private static int Pass1(int v) => v + 1;
    private static int Pass2(int v) => v;
    private static int Pass3(int v) => v;
    private static int Pass4(int v) => v;
    private static int Pass5(int v) => v;

    [Benchmark(Description = "E02 Direct 0")]
    public ValueTask<int> E02Direct0() =>
        ((IRequestHandler<Zp0, int>)_eH0).HandleAsync(_zp0, CancellationToken.None);

    [Benchmark(Description = "E02 Direct 1")]
    public ValueTask<int> E02Direct1() =>
        ((IPipelineBehavior<Zp1, int>)_eB1).HandleAsync(_zp1, new E02T1(_eH1), CancellationToken.None);

    [Benchmark(Description = "E02 Direct 3")]
    public ValueTask<int> E02Direct3() =>
        ((IPipelineBehavior<Zp3, int>)_e31).HandleAsync(_zp3, new E02N3a(_e32, _e33, _eH3), CancellationToken.None);

    [Benchmark(Description = "E02 Direct 5")]
    public ValueTask<int> E02Direct5() =>
        ((IPipelineBehavior<Zp5, int>)_e51).HandleAsync(_zp5, new E02N5a(_e52, _e53, _e54, _e55, _eH5), CancellationToken.None);

    private readonly struct E02T1(Zp1Handler handler) : IRequestContinuation<Zp1, int>
    {
        public ValueTask<int> InvokeAsync(Zp1 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IRequestHandler<Zp1, int>)handler).HandleAsync(request, cancellationToken);
        }
    }

    private readonly struct E02N3a(Zb2<Zp3, int> b2, Zb3<Zp3, int> b3, Zp3Handler handler) : IRequestContinuation<Zp3, int>
    {
        public ValueTask<int> InvokeAsync(Zp3 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IPipelineBehavior<Zp3, int>)b2).HandleAsync(request, new E02N3b(b3, handler), cancellationToken);
        }
    }

    private readonly struct E02N3b(Zb3<Zp3, int> b3, Zp3Handler handler) : IRequestContinuation<Zp3, int>
    {
        public ValueTask<int> InvokeAsync(Zp3 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IPipelineBehavior<Zp3, int>)b3).HandleAsync(request, new E02T3(handler), cancellationToken);
        }
    }

    private readonly struct E02T3(Zp3Handler handler) : IRequestContinuation<Zp3, int>
    {
        public ValueTask<int> InvokeAsync(Zp3 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IRequestHandler<Zp3, int>)handler).HandleAsync(request, cancellationToken);
        }
    }

    private readonly struct E02N5a(Zb2<Zp5, int> b2, Zb3<Zp5, int> b3, Zb4<Zp5, int> b4, Zb5<Zp5, int> b5, Zp5Handler handler) : IRequestContinuation<Zp5, int>
    {
        public ValueTask<int> InvokeAsync(Zp5 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IPipelineBehavior<Zp5, int>)b2).HandleAsync(request, new E02N5b(b3, b4, b5, handler), cancellationToken);
        }
    }

    private readonly struct E02N5b(Zb3<Zp5, int> b3, Zb4<Zp5, int> b4, Zb5<Zp5, int> b5, Zp5Handler handler) : IRequestContinuation<Zp5, int>
    {
        public ValueTask<int> InvokeAsync(Zp5 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IPipelineBehavior<Zp5, int>)b3).HandleAsync(request, new E02N5c(b4, b5, handler), cancellationToken);
        }
    }

    private readonly struct E02N5c(Zb4<Zp5, int> b4, Zb5<Zp5, int> b5, Zp5Handler handler) : IRequestContinuation<Zp5, int>
    {
        public ValueTask<int> InvokeAsync(Zp5 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IPipelineBehavior<Zp5, int>)b4).HandleAsync(request, new E02N5d(b5, handler), cancellationToken);
        }
    }

    private readonly struct E02N5d(Zb5<Zp5, int> b5, Zp5Handler handler) : IRequestContinuation<Zp5, int>
    {
        public ValueTask<int> InvokeAsync(Zp5 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IPipelineBehavior<Zp5, int>)b5).HandleAsync(request, new E02T5(handler), cancellationToken);
        }
    }

    private readonly struct E02T5(Zp5Handler handler) : IRequestContinuation<Zp5, int>
    {
        public ValueTask<int> InvokeAsync(Zp5 request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ((IRequestHandler<Zp5, int>)handler).HandleAsync(request, cancellationToken);
        }
    }

    [Benchmark(Description = "MediatR 0")]
    public Task<int> MediatR0() => _mr.Send(_mrP0);

    [Benchmark(Description = "MediatR 1")]
    public Task<int> MediatR1() => _mr.Send(_mrP1);

    [Benchmark(Description = "MediatR 3")]
    public Task<int> MediatR3() => _mr.Send(_mrP3);

    [Benchmark(Description = "MediatR 5")]
    public Task<int> MediatR5() => _mr.Send(_mrP5);

    [Benchmark(Description = "Mo 0")]
    public ValueTask<int> Mo0() => _mo.Send(_moP0);

    [Benchmark(Description = "Mo 1")]
    public ValueTask<int> Mo1() => _mo.Send(_moP1);

    [Benchmark(Description = "Mo 3")]
    public ValueTask<int> Mo3() => _mo.Send(_moP3);

    [Benchmark(Description = "Mo 5")]
    public ValueTask<int> Mo5() => _mo.Send(_moP5);

    [Benchmark(Description = "DispatchR 0")]
    public ValueTask<int> Dr0() => _dr.Send<DrP0, ValueTask<int>>(_drP0, CancellationToken.None);

    [Benchmark(Description = "DispatchR 1")]
    public ValueTask<int> Dr1() => _dr.Send<DrP1, ValueTask<int>>(_drP1, CancellationToken.None);

    [Benchmark(Description = "DispatchR 3")]
    public ValueTask<int> Dr3() => _dr.Send<DrP3, ValueTask<int>>(_drP3, CancellationToken.None);

    [Benchmark(Description = "DispatchR 5")]
    public ValueTask<int> Dr5() => _dr.Send<DrP5, ValueTask<int>>(_drP5, CancellationToken.None);

    [Benchmark(Description = "DSoft 0")]
    public ValueTask<int> Ds0() => _ds.Send(_dsP0);

    [Benchmark(Description = "IH 0")]
    public ValueTask<int> Ih0() => _ih0.HandleAsync(_ihP0);

    [Benchmark(Description = "IH 1")]
    public ValueTask<int> Ih1() => _ih1.HandleAsync(_ihP1);

    [Benchmark(Description = "IH 3")]
    public ValueTask<int> Ih3() => _ih3.HandleAsync(_ihP3);

    [Benchmark(Description = "IH 5")]
    public ValueTask<int> Ih5() => _ih5.HandleAsync(_ihP5);
}
