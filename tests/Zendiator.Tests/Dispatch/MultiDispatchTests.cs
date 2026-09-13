using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class MultiDispatchTests
{
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddScoped<Trace>();
        services.AddScoped<AuditLog>();
        services.AddScoped<Gate>();
        services.AddZendiator();
        return services;
    }

    [Fact]
    public async Task Single_handler_multi_returns_one_result()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var first = await mediator.SendAllAsync(new GetSolo(21));
        var second = await mediator.SendAllAsync(new GetSolo(21));
        Assert.Equal([42], first);
        Assert.Equal([42], second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Send_all_collects_results_in_handler_order()
    {
        VendorAQuotesHandler.Calls = 0;
        VendorBQuotesHandler.Calls = 0;
        VendorCQuotesHandler.Calls = 0;
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var quotes = await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAllAsync(new GetQuotes("P1"));
        Assert.Equal([new Quote("A", 110m), new Quote("C", 165m), new Quote("B", 220m)], quotes);
        Assert.Equal(1, VendorAQuotesHandler.Calls);
        Assert.Equal(1, VendorBQuotesHandler.Calls);
        Assert.Equal(1, VendorCQuotesHandler.Calls);
    }

    [Fact]
    public async Task Class_requests_share_one_reference_without_copying()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var log = new TraceLog();
        var results = await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAllAsync(new GetTrace(log));
        Assert.Equal(["r1", "r2"], results);
        Assert.Equal(["first", "second"], log.Entries);
    }

    [Fact]
    public async Task Branch_request_replacement_stays_within_its_branch()
    {
        VersionHandlerA.SeenA = -1;
        VersionHandlerB.SeenB = -1;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var results = await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAllAsync(new GetVersion(1));
        Assert.Equal([101, 101], results);
        Assert.Equal(101, VersionHandlerA.SeenA);
        Assert.Equal(101, VersionHandlerB.SeenB);
    }

    [Fact]
    public async Task Short_circuit_skips_handlers_but_completes_all_branches()
    {
        MaybeHandlerA.Calls = 0;
        MaybeHandlerB.Calls = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal([0, 0], await mediator.SendAllAsync(new GetMaybe(false)));
        Assert.Equal(0, MaybeHandlerA.Calls);
        Assert.Equal(0, MaybeHandlerB.Calls);
        Assert.Equal([1, 2], await mediator.SendAllAsync(new GetMaybe(true)));
        Assert.Equal(1, MaybeHandlerA.Calls);
        Assert.Equal(1, MaybeHandlerB.Calls);
    }

    [Fact]
    public async Task First_failure_stops_without_partial_results()
    {
        RiskySecondHandler.Calls = 0;
        var services = Services();
        services.AddScoped<RiskySecondHandler>(_ => throw new InvalidOperationException("must not construct"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.Same(RiskyFirstHandler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAllAsync(new GetRisky())));
        Assert.Equal(0, RiskySecondHandler.Calls);
    }

    [Fact]
    public async Task Void_multi_completes_without_unit()
    {
        BroadcastHandlerA.Got.Clear();
        BroadcastHandlerB.Got.Clear();
        BroadcastBehavior.Calls = 0;
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAllAsync(new Broadcast("hi"));
        Assert.Equal(["a:hi"], BroadcastHandlerA.Got);
        Assert.Equal(["b:hi"], BroadcastHandlerB.Got);
        Assert.Equal(2, BroadcastBehavior.Calls);
    }

    [Fact]
    public async Task Open_multi_fans_out_by_type_argument()
    {
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(["a:1", "b:1"], await mediator.SendAllAsync(new FanOut<int>(1)));
        Assert.Equal(["a:2", "b:2"], await mediator.SendAllAsync(new FanOut<string>(2)));
    }

    [Fact]
    public async Task Open_void_multi_completes_without_unit()
    {
        WipeHandlerA<int>.Got.Clear();
        WipeHandlerB<int>.Got.Clear();
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().SendAllAsync(new Wipe<int>([1, 2]));
        Assert.Equal([1, 2], WipeHandlerA<int>.Got);
        Assert.Equal([1, 2], WipeHandlerB<int>.Got);
    }
}
