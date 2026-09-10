using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class SyncDispatchTests
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

    private static int Measure<T>(IZendiator sender, T value, CancellationToken cancellationToken)
        where T : allows ref struct =>
        sender.SendSync(new Box<T>(value), cancellationToken);

    [Fact]
    public void Span_backed_request_returns_synchronously()
    {
        ParseHandler.Calls = 0;
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Span<byte> buffer = stackalloc byte[8];
        Assert.Equal(1008, mediator.SendSync(new ParseRequest(buffer)));
        Assert.Equal(1, ParseHandler.Calls);
        Assert.Equal(["sync", "tag", "final", "/final", "/tag", "/sync"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }

    [Fact]
    public void Empty_span_short_circuits_without_handler()
    {
        ParseHandler.Calls = 0;
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(-1, scope.ServiceProvider.GetRequiredService<IZendiator>().SendSync(new ParseRequest(ReadOnlySpan<byte>.Empty)));
        Assert.Equal(0, ParseHandler.Calls);
    }

    [Fact]
    public void Void_sync_command_needs_no_unit()
    {
        FlushHandler.Flushed.Clear();
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IZendiator>().SendSync(new FlushRequest(3));
        Assert.Equal([3], FlushHandler.Flushed);
        Assert.Equal(["sync-void", "/sync-void"], scope.ServiceProvider.GetRequiredService<Trace>().Events);
    }

    [Fact]
    public void Zero_behavior_sync_route_resolves_directly()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal("label-5", scope.ServiceProvider.GetRequiredService<IZendiator>().SendSync(new GetLabel(5)));
    }

    [Fact]
    public void Sync_retry_and_request_replacement_compose()
    {
        AddOneHandler.Calls = 0;
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        // Retry(20) outside Replace(21): each attempt sees 1 + 10, then + 1.
        Assert.Equal(12, scope.ServiceProvider.GetRequiredService<IZendiator>().SendSync(new AddOne(1)));
        Assert.Equal(2, AddOneHandler.Calls);
    }

    [Fact]
    public void Sync_behavior_can_replace_the_token_downstream()
    {
        SeenSyncHandler.Seen = default;
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IZendiator>().SendSync(new SeenSync());
        Assert.NotEqual(CancellationToken.None, SeenSyncHandler.Seen);
    }

    [Fact]
    public void Sync_pre_cancellation_is_observed()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<IZendiator>().SendSync(new AddOne(1), cancellation.Token));
    }

    [Fact]
    public void Explicit_sync_handler_works_without_boxing()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(7, scope.ServiceProvider.GetRequiredService<IZendiator>().SendSync(new ExplicitSync()));
    }

    [Fact]
    public void Generic_ref_request_flows_from_unbound_callers()
    {
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal("Int32".Length, Measure(mediator, 42, CancellationToken.None));
        Span<byte> span = stackalloc byte[5];
        Assert.True(Measure(mediator, span, CancellationToken.None) > 0);
    }

    [Fact]
    public void Sync_multi_collects_in_order_with_branch_pipelines()
    {
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal([6, 7], mediator.SendAllSync(new SumSync(3, 2)));
        ResetSyncHandlerA.Got.Clear();
        ResetSyncHandlerB.Got.Clear();
        mediator.SendAllSync(new ResetSync("db"));
        Assert.Equal(["a:db"], ResetSyncHandlerA.Got);
        Assert.Equal(["b:db"], ResetSyncHandlerB.Got);
    }

    [Fact]
    public void Sync_multi_generic_ref_combines()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var results = scope.ServiceProvider.GetRequiredService<IZendiator>().SendAllSync(new MultiRow<int>(7, 9));
        Assert.Equal(["a:7", "b:7"], results);
    }

    [Fact]
    public void Sync_transient_handlers_resolve_per_send()
    {
        var services = Services();
        services.AddTransient<AddOneHandler>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        mediator.SendSync(new AddOne(1));
        var first = scope.ServiceProvider.GetRequiredService<AddOneHandler>().Id;
        mediator.SendSync(new AddOne(1));
        Assert.NotEqual(first, scope.ServiceProvider.GetRequiredService<AddOneHandler>().Id);
    }
}
