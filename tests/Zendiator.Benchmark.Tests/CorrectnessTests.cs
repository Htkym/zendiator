using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.Benchmarks;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zendiator.Benchmark.Tests;

/// <summary>Verifies every benchmark path dispatches correctly before measuring.
/// Same input, same output, same invocation counts.</summary>
public sealed class RequestCorrectnessTests
{
    [Fact]
    public async Task All_request_paths_return_expected_value()
    {
        using var zr = ZrHost.CreateScoped();
        using var zrScope = zr.CreateScope();
        var zrMediator = zrScope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.IZendiator>();
        Assert.Equal(42, await zrMediator.SendAsync(new Zp0(41)));
        Assert.Equal(42, await zrMediator.SendAsync(new Zp1(41)));
        Assert.Equal(42, await zrMediator.SendAsync(new Zp3(41)));
        Assert.Equal(42, await zrMediator.SendAsync(new Zp5(41)));
        Assert.Equal(42, await zrMediator.SendAsync(new ZpAsync(41)));
        Assert.Equal(42, await zrScope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.Zendiator>().SendAsync(new Zp0(41)));

        using var zrSingle = ZrHost.CreateSingleton();
        var zrSingleMediator = zrSingle.GetRequiredService<global::Zendiator.Benchmarks.IZendiator>();
        Assert.Equal(42, await zrSingleMediator.SendAsync(new Zp0(41)));
        Assert.Equal(42, await zrSingleMediator.SendAsync(new Zp1(41)));
        Assert.Equal(42, await zrSingleMediator.SendAsync(new Zp3(41)));
        Assert.Equal(42, await zrSingleMediator.SendAsync(new Zp5(41)));

        using var mr = MrHost.CreateProvider();
        using var mrScope = mr.CreateScope();
        var mrMediator = mrScope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        Assert.Equal(42, await mrMediator.Send(new MrP0(41)));
        Assert.Equal(42, await mrMediator.Send(new MrP1(41)));
        Assert.Equal(42, await mrMediator.Send(new MrP3(41)));
        Assert.Equal(42, await mrMediator.Send(new MrP5(41)));
        Assert.Equal(42, await mrMediator.Send(new MrAsyncPing(41)));

        using var mo = MoHost.CreateProvider();
        using var moScope = mo.CreateScope();
        var moMediator = moScope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        Assert.Equal(42, await moMediator.Send(new MoP0(41)));
        Assert.Equal(42, await moMediator.Send(new MoP1(41)));
        Assert.Equal(42, await moMediator.Send(new MoP3(41)));
        Assert.Equal(42, await moMediator.Send(new MoP5(41)));
        Assert.Equal(42, await moMediator.Send(new MoAsyncPing(41)));
        Assert.Equal(42, await moScope.ServiceProvider.GetRequiredService<global::Mediator.Mediator>().Send(new MoP0(41)));

        using var dr = DrHost.CreateProvider();
        using var drScope = dr.CreateScope();
        var drMediator = drScope.ServiceProvider.GetRequiredService<global::DispatchR.IMediator>();
        Assert.Equal(42, await drMediator.Send<DrP0, ValueTask<int>>(new DrP0(41), CancellationToken.None));
        Assert.Equal(42, await drMediator.Send<DrP1, ValueTask<int>>(new DrP1(41), CancellationToken.None));
        Assert.Equal(42, await drMediator.Send<DrP3, ValueTask<int>>(new DrP3(41), CancellationToken.None));
        Assert.Equal(42, await drMediator.Send<DrP5, ValueTask<int>>(new DrP5(41), CancellationToken.None));

        using var ds = DsHost.CreateProvider();
        using var dsScope = ds.CreateScope();
        var dsMediator = dsScope.ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();
        Assert.Equal(42, await dsMediator.Send(new DsPing(41)));
        Assert.Equal(42, await dsMediator.Send(new DsP0(41)));

        using var fo = FoHost.CreateProvider();
        using var foScope = fo.CreateScope();
        var foMediator = foScope.ServiceProvider.GetRequiredService<global::Foundatio.Mediator.IMediator>();
        Assert.Equal(42, foMediator.Invoke<int>(new FoPing(41)));

        using var ih = IhHost.CreateProvider();
        using var ihScope = ih.CreateScope();
        Assert.Equal(42, await ihScope.ServiceProvider.GetRequiredService<IhPing.Handler>().HandleAsync(new IhPing.Query(41)));
        Assert.Equal(42, await ihScope.ServiceProvider.GetRequiredService<IhP0.Handler>().HandleAsync(new IhP0.Query(41)));
        Assert.Equal(42, await ihScope.ServiceProvider.GetRequiredService<IhP1.Handler>().HandleAsync(new IhP1.Query(41)));
        Assert.Equal(42, await ihScope.ServiceProvider.GetRequiredService<IhP3.Handler>().HandleAsync(new IhP3.Query(41)));
        Assert.Equal(42, await ihScope.ServiceProvider.GetRequiredService<IhP5.Handler>().HandleAsync(new IhP5.Query(41)));
    }

    [Fact]
    public async Task Pipeline_invocation_counts_match_depth()
    {
        using var zr = ZrHost.CreateScoped();
        using var zrScope = zr.CreateScope();
        var zrMediator = zrScope.ServiceProvider.GetRequiredService<global::Zendiator.Benchmarks.IZendiator>();
        ZrCounters.Reset();
        await zrMediator.SendAsync(new Zp0(41));
        Assert.Equal((0, 0, 0, 0, 0), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));
        ZrCounters.Reset();
        await zrMediator.SendAsync(new Zp1(41));
        Assert.Equal((1, 0, 0, 0, 0), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));
        ZrCounters.Reset();
        await zrMediator.SendAsync(new Zp3(41));
        Assert.Equal((1, 1, 1, 0, 0), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));
        ZrCounters.Reset();
        await zrMediator.SendAsync(new Zp5(41));
        Assert.Equal((1, 1, 1, 1, 1), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));

        using var zrSingle = ZrHost.CreateSingleton();
        var zrSingleMediator = zrSingle.GetRequiredService<global::Zendiator.Benchmarks.IZendiator>();
        ZrCounters.Reset();
        await zrSingleMediator.SendAsync(new Zp0(41));
        Assert.Equal((0, 0, 0, 0, 0), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));
        ZrCounters.Reset();
        await zrSingleMediator.SendAsync(new Zp1(41));
        Assert.Equal((1, 0, 0, 0, 0), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));
        ZrCounters.Reset();
        await zrSingleMediator.SendAsync(new Zp3(41));
        Assert.Equal((1, 1, 1, 0, 0), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));
        ZrCounters.Reset();
        await zrSingleMediator.SendAsync(new Zp5(41));
        Assert.Equal((1, 1, 1, 1, 1), (ZrCounters.B1, ZrCounters.B2, ZrCounters.B3, ZrCounters.B4, ZrCounters.B5));

        using var mr = MrHost.CreateProvider();
        using var mrScope = mr.CreateScope();
        var mrMediator = mrScope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        MrCounters.Reset();
        await mrMediator.Send(new MrP0(41));
        Assert.Equal((0, 0, 0, 0, 0), (MrCounters.B1, MrCounters.B2, MrCounters.B3, MrCounters.B4, MrCounters.B5));
        MrCounters.Reset();
        await mrMediator.Send(new MrP1(41));
        Assert.Equal((1, 0, 0, 0, 0), (MrCounters.B1, MrCounters.B2, MrCounters.B3, MrCounters.B4, MrCounters.B5));
        MrCounters.Reset();
        await mrMediator.Send(new MrP3(41));
        Assert.Equal((1, 1, 1, 0, 0), (MrCounters.B1, MrCounters.B2, MrCounters.B3, MrCounters.B4, MrCounters.B5));
        MrCounters.Reset();
        await mrMediator.Send(new MrP5(41));
        Assert.Equal((1, 1, 1, 1, 1), (MrCounters.B1, MrCounters.B2, MrCounters.B3, MrCounters.B4, MrCounters.B5));

        using var mo = MoHost.CreateProvider();
        using var moScope = mo.CreateScope();
        var moMediator = moScope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        MoCounters.Reset();
        await moMediator.Send(new MoP0(41));
        Assert.Equal((0, 0, 0, 0, 0), (MoCounters.B1, MoCounters.B2, MoCounters.B3, MoCounters.B4, MoCounters.B5));
        MoCounters.Reset();
        await moMediator.Send(new MoP1(41));
        Assert.Equal((1, 0, 0, 0, 0), (MoCounters.B1, MoCounters.B2, MoCounters.B3, MoCounters.B4, MoCounters.B5));
        MoCounters.Reset();
        await moMediator.Send(new MoP3(41));
        Assert.Equal((1, 1, 1, 0, 0), (MoCounters.B1, MoCounters.B2, MoCounters.B3, MoCounters.B4, MoCounters.B5));
        MoCounters.Reset();
        await moMediator.Send(new MoP5(41));
        Assert.Equal((1, 1, 1, 1, 1), (MoCounters.B1, MoCounters.B2, MoCounters.B3, MoCounters.B4, MoCounters.B5));

        using var dr = DrHost.CreateProvider();
        using var drScope = dr.CreateScope();
        var drMediator = drScope.ServiceProvider.GetRequiredService<global::DispatchR.IMediator>();
        DrCounters.Reset();
        await drMediator.Send<DrP0, ValueTask<int>>(new DrP0(41), CancellationToken.None);
        Assert.Equal(0, DrCounters.B1 + DrCounters.B2 + DrCounters.B3 + DrCounters.B4 + DrCounters.B5 + DrCounters.B6 + DrCounters.B7 + DrCounters.B8 + DrCounters.B9);
        DrCounters.Reset();
        await drMediator.Send<DrP1, ValueTask<int>>(new DrP1(41), CancellationToken.None);
        Assert.Equal(1, DrCounters.B1);
        DrCounters.Reset();
        await drMediator.Send<DrP3, ValueTask<int>>(new DrP3(41), CancellationToken.None);
        Assert.Equal(3, DrCounters.B2 + DrCounters.B3 + DrCounters.B4);
        DrCounters.Reset();
        await drMediator.Send<DrP5, ValueTask<int>>(new DrP5(41), CancellationToken.None);
        Assert.Equal(5, DrCounters.B5 + DrCounters.B6 + DrCounters.B7 + DrCounters.B8 + DrCounters.B9);

        using var ds = DsHost.CreateProvider();
        using var dsScope = ds.CreateScope();
        var dsMediator = dsScope.ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();
        Assert.Equal(42, await dsMediator.Send(new DsP0(41)));

        using var ih = IhHost.CreateProvider();
        using var ihScope = ih.CreateScope();
        IhCounters.Reset();
        await ihScope.ServiceProvider.GetRequiredService<IhP0.Handler>().HandleAsync(new IhP0.Query(41));
        Assert.Equal((0, 0, 0, 0, 0), (IhCounters.B1, IhCounters.B2, IhCounters.B3, IhCounters.B4, IhCounters.B5));
        IhCounters.Reset();
        await ihScope.ServiceProvider.GetRequiredService<IhP1.Handler>().HandleAsync(new IhP1.Query(41));
        Assert.Equal((1, 0, 0, 0, 0), (IhCounters.B1, IhCounters.B2, IhCounters.B3, IhCounters.B4, IhCounters.B5));
        IhCounters.Reset();
        await ihScope.ServiceProvider.GetRequiredService<IhP3.Handler>().HandleAsync(new IhP3.Query(41));
        Assert.Equal((1, 1, 1, 0, 0), (IhCounters.B1, IhCounters.B2, IhCounters.B3, IhCounters.B4, IhCounters.B5));
        IhCounters.Reset();
        await ihScope.ServiceProvider.GetRequiredService<IhP5.Handler>().HandleAsync(new IhP5.Query(41));
        Assert.Equal((1, 1, 1, 1, 1), (IhCounters.B1, IhCounters.B2, IhCounters.B3, IhCounters.B4, IhCounters.B5));
    }

    [Fact]
    public async Task Notification_subscriber_counts_match()
    {
        using var mr = MrHost.CreateProvider();
        using var mrScope = mr.CreateScope();
        var mrMediator = mrScope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>();
        MrEventCounters.Reset();
        await mrMediator.Publish(new MrEv1(41));
        Assert.Equal(1, MrEventCounters.H1);
        MrEventCounters.Reset();
        await mrMediator.Publish(new MrEv4(41));
        Assert.Equal(4, MrEventCounters.H4);
        MrEventCounters.Reset();
        await mrMediator.Publish(new MrEv16(41));
        Assert.Equal(16, MrEventCounters.H16);

        using var mo = MoHost.CreateProvider();
        using var moScope = mo.CreateScope();
        var moMediator = moScope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        MoEventCounters.Reset();
        await moMediator.Publish(new MoEv1(41));
        Assert.Equal(1, MoEventCounters.H1);
        MoEventCounters.Reset();
        await moMediator.Publish(new MoEv4(41));
        Assert.Equal(4, MoEventCounters.H4);
        MoEventCounters.Reset();
        await moMediator.Publish(new MoEv16(41));
        Assert.Equal(16, MoEventCounters.H16);

        using var dr = DrHost.CreateProvider();
        using var drScope = dr.CreateScope();
        var drMediator = drScope.ServiceProvider.GetRequiredService<global::DispatchR.IMediator>();
        DrEventCounters.Reset();
        await drMediator.Publish(new DrEv1(41), CancellationToken.None);
        Assert.Equal(1, DrEventCounters.H1);
        DrEventCounters.Reset();
        await drMediator.Publish(new DrEv4(41), CancellationToken.None);
        Assert.Equal(4, DrEventCounters.H4);
    }

    [Fact]
    public async Task Runtime_dispatch_returns_expected_value()
    {
        using var mr = MrHost.CreateProvider();
        using var mrScope = mr.CreateScope();
        var mrSender = mrScope.ServiceProvider.GetRequiredService<global::MediatR.ISender>();
        Assert.Equal(42, await mrSender.Send((object)new MrP0(41)));

        using var mo = MoHost.CreateProvider();
        using var moScope = mo.CreateScope();
        var moMediator = moScope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        Assert.Equal(42, await moMediator.Send((object)new MoP0(41)));

        using var ds = DsHost.CreateProvider();
        using var dsScope = ds.CreateScope();
        var dsSender = dsScope.ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.ISender>();
        Assert.Equal(42, await dsSender.Send((object)new DsPing(41)));
    }
}
