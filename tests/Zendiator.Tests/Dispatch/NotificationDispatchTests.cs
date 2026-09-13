using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class NotificationDispatchTests
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
    public async Task Publish_delivers_to_all_subscribers_in_order()
    {
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IZendiator>().PublishAsync(new UserCreated(7));
        Assert.Equal(["metrics:7", "alpha", "provision:7", "mail:7"], scope.ServiceProvider.GetRequiredService<AuditLog>().Events);
    }

    [Fact]
    public async Task Publish_alias_matches_publish_async()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await mediator.Publish(new UserCreated(8));
        Assert.Equal(["metrics:8", "alpha", "provision:8", "mail:8"], scope.ServiceProvider.GetRequiredService<AuditLog>().Events);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await mediator.PublishAsync((UserCreated)null!));
    }

    [Fact]
    public async Task Known_notification_without_subscribers_completes()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await mediator.PublishAsync(new Lonely());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mediator.PublishAsync(new Lonely(), cancellation.Token));
    }

    [Fact]
    public async Task Next_subscriber_starts_after_the_previous_completes()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var gate = scope.ServiceProvider.GetRequiredService<Gate>();
        var pending = mediator.PublishAsync(new SyncPoint("s"));
        await gate.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["first-start"], gate.Events);
        gate.Release.TrySetResult();
        await pending;
        Assert.Equal(["first-start", "first-end", "second"], gate.Events);
    }

    [Fact]
    public async Task First_failure_stops_without_constructing_later_subscribers()
    {
        FragileSecondHandler.Calls = 0;
        var services = Services();
        services.AddScoped<FragileSecondHandler>(_ => throw new InvalidOperationException("must not construct"));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Assert.Same(FragileFirstHandler.Failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await mediator.PublishAsync(new Fragile(1))));
        Assert.Equal(0, FragileSecondHandler.Calls);
    }

    [Fact]
    public async Task Cancellation_between_subscribers_stops_dispatch()
    {
        CancelFirstHandler.Live = new CancellationTokenSource();
        CancelSecondHandler.Calls = 0;
        try
        {
            await using var provider = Services().BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mediator.PublishAsync(new CancelMid(), CancelFirstHandler.Live.Token));
            Assert.Equal(0, CancelSecondHandler.Calls);
        }
        finally
        {
            CancelFirstHandler.Live?.Dispose();
            CancelFirstHandler.Live = null;
        }
    }

    [Fact]
    public async Task Type_erased_publish_uses_the_runtime_type()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        INotification erased = new UserCreated(9);
        await mediator.PublishAsync(erased);
        Assert.Equal(["metrics:9", "alpha", "provision:9", "mail:9"], scope.ServiceProvider.GetRequiredService<AuditLog>().Events);
    }

    [Fact]
    public async Task Struct_notifications_use_the_typed_route()
    {
        TickHandler.Sum = 0;
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await mediator.PublishAsync(new Tick(3));
        await mediator.Publish(new Tick(4));
        Assert.Equal(7, TickHandler.Sum);
    }

    [Fact]
    public async Task Typed_and_erased_routes_use_different_keys()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var log = scope.ServiceProvider.GetRequiredService<AuditLog>();
        BaseNote widened = new DerivedNote(1, "x");
        await mediator.PublishAsync(widened);
        Assert.Equal(["base:1"], log.Events);
        log.Events.Clear();
        INotification erased = new DerivedNote(1, "x");
        await mediator.PublishAsync(erased);
        Assert.Equal(["derived:1:x"], log.Events);
    }

    [Fact]
    public async Task Open_notifications_publish_by_type_argument()
    {
        ChangedHandler<int>.Seen.Clear();
        ChangedHandler<string>.Seen.Clear();
        await using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        await mediator.PublishAsync(new Changed<int>(1));
        await mediator.Publish(new Changed<string>("a"));
        Assert.Equal(["1"], ChangedHandler<int>.Seen);
        Assert.Equal(["a"], ChangedHandler<string>.Seen);
    }
}
