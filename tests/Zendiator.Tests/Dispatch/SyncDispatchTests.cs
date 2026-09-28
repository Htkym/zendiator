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
        var canceled = Assert.ThrowsAny<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<IZendiator>().SendSync(new AddOne(1), cancellation.Token));
        Assert.Equal(cancellation.Token, canceled.CancellationToken);
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
    public void Sync_transient_handlers_are_captured_once_per_mediator()
    {
        var services = Services();
        var calls = 0;
        services.AddTransient<AddOneHandler>(_ => { calls++; return new(); });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(12, mediator.SendSync(new AddOne(1)));
        Assert.Equal(12, mediator.SendSync(new AddOne(1)));
        Assert.Equal(1, calls);
        Assert.NotSame(scope.ServiceProvider.GetRequiredService<AddOneHandler>(), scope.ServiceProvider.GetRequiredService<AddOneHandler>());
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Span_results_preserve_order_and_unused_capacity()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Span<int> buffer = stackalloc int[] { -1, -2, -3 };
        Assert.Equal(2, mediator.SendAllSync(new SumSync(3, 2), buffer, default));
        Assert.Equal([6, 7, -3], buffer.ToArray());
        // The existing two-argument default call must remain unambiguous.
        Assert.Equal([6, 7], mediator.SendAllSync(new SumSync(3, 2), default));
        string[] references = ["old", "old", "untouched"];
        Assert.Equal(2, mediator.SendAllSync(new MultiRow<Span<int>>(7, buffer), references, default));
        Assert.Equal(["a:7", "b:7", "untouched"], references);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Short_span_fails_before_resolving_dependencies(int length)
    {
        var services = Services();
        services.AddTransient<SumSyncHandlerA>(_ => throw new InvalidOperationException("must not resolve"));
        services.AddTransient<Trace>(_ => throw new InvalidOperationException("must not resolve behavior dependencies"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var buffer = new int[length];
        var exception = Assert.Throws<ArgumentException>(() => mediator.SendAllSync(new SumSync(3, 2), buffer, default));
        Assert.Equal("destination", exception.ParamName);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal(canceled.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            mediator.SendAllSync(new SumSync(3, 2), buffer, canceled.Token)).CancellationToken);
    }

    [Fact]
    public void Span_results_do_not_overwrite_overlapping_input_until_all_handlers_finish()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        Span<int> buffer = stackalloc int[] { 3, -1, -2 };
        Assert.Equal(2, scope.ServiceProvider.GetRequiredService<IZendiator>().SendAllSync(new SpanMulti(buffer), buffer, default));
        Assert.Equal([13, 23, -2], buffer.ToArray());
    }

    [Fact]
    public void Span_results_leave_destination_unchanged_when_a_later_handler_fails()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var buffer = new[] { 3, -1, -2 };
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal("second", Assert.Throws<InvalidOperationException>(() =>
            mediator.SendAllSync(new SpanMulti(buffer, failSecond: true), buffer, default)).Message);
        Assert.Equal([3, -1, -2], buffer);
    }

    [Fact]
    public void Cancellation_between_span_branches_leaves_destination_unchanged()
    {
        using var provider = Services().BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var canceled = new CancellationTokenSource();
        var buffer = new[] { 3, -1, -2 };
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Equal(canceled.Token, Assert.ThrowsAny<OperationCanceledException>(() =>
            mediator.SendAllSync(new SpanMulti(buffer, afterFirst: canceled.Cancel), buffer, canceled.Token)).CancellationToken);
        Assert.Equal([3, -1, -2], buffer);
    }
}
