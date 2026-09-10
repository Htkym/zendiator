using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zendiator.Benchmarks;

namespace Zendiator.Benchmark.Tests;

// Same-meaning observable lanes; multiple inputs pin the transform.

public sealed class ObservableCorrectnessTests
{
    [Fact]
    public async Task Observable_values_match_in_every_lane()
    {
        using var mo = MoHost.CreateProvider();
        using var moScope = mo.CreateScope();
        var moMediator = moScope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        Assert.Equal(41, await moMediator.Send(new MoO0(41)));
        Assert.Equal(141, await moMediator.Send(new MoO1(41)));
        Assert.Equal(341, await moMediator.Send(new MoO3(41)));
        Assert.Equal(541, await moMediator.Send(new MoO5(41)));
        Assert.Equal(105, await moMediator.Send(new MoO1(5)));
        Assert.Equal(300, await moMediator.Send(new MoO3(0)));

        using var dr = DrHost.CreateProvider();
        using var drScope = dr.CreateScope();
        var drMediator = drScope.ServiceProvider.GetRequiredService<global::DispatchR.IMediator>();
        Assert.Equal(41, await drMediator.Send<DrO0, ValueTask<int>>(new DrO0(41), CancellationToken.None));
        Assert.Equal(141, await drMediator.Send<DrO1, ValueTask<int>>(new DrO1(41), CancellationToken.None));
        Assert.Equal(341, await drMediator.Send<DrO3, ValueTask<int>>(new DrO3(41), CancellationToken.None));
        Assert.Equal(541, await drMediator.Send<DrO5, ValueTask<int>>(new DrO5(41), CancellationToken.None));
        Assert.Equal(300, await drMediator.Send<DrO3, ValueTask<int>>(new DrO3(0), CancellationToken.None));

        using var ds = DsHost.CreateProvider();
        using var dsScope = ds.CreateScope();
        var dsMediator = dsScope.ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();
        Assert.Equal(41, await dsMediator.Send(new DsOz0(41)));

        using var dsObs = global::Zendiator.DSoftObs.DsObsHost.CreateProvider();
        using var dsObsScope = dsObs.CreateScope();
        var dsObsMediator = dsObsScope.ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();
        Assert.Equal(141, await dsObsMediator.Send(new global::Zendiator.DSoftObs.DsO0(41)));
        Assert.Equal(100, await dsObsMediator.Send(new global::Zendiator.DSoftObs.DsO0(0)));

        using var ih = IhHost.CreateProvider();
        using var ihScope = ih.CreateScope();
        Assert.Equal(41, await ihScope.ServiceProvider.GetRequiredService<IhO0.Handler>().HandleAsync(new IhO0.Query(41)));
        Assert.Equal(141, await ihScope.ServiceProvider.GetRequiredService<IhO1.Handler>().HandleAsync(new IhO1.Query(41)));
        Assert.Equal(341, await ihScope.ServiceProvider.GetRequiredService<IhO3.Handler>().HandleAsync(new IhO3.Query(41)));
        Assert.Equal(541, await ihScope.ServiceProvider.GetRequiredService<IhO5.Handler>().HandleAsync(new IhO5.Query(41)));
    }

    [Fact]
    public async Task Existing_rows_keep_pass_through_meaning()
    {
        using var ds = DsHost.CreateProvider();
        using var dsScope = ds.CreateScope();
        var dsMediator = dsScope.ServiceProvider.GetRequiredService<global::DSoftStudio.Mediator.Abstractions.IMediator>();
        Assert.Equal(42, await dsMediator.Send(new DsP0(41)));
        Assert.Equal(42, await dsMediator.Send(new DsPing(41)));

        using var mo = MoHost.CreateProvider();
        using var moScope = mo.CreateScope();
        var moMediator = moScope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>();
        Assert.Equal(42, await moMediator.Send(new MoP0(41)));
        Assert.Equal(42, await moMediator.Send(new MoP5(41)));
    }
}
