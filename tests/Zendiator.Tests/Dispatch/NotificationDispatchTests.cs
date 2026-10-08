using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed class NotificationDispatchTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Publish_consumes_value_task_source_once_and_restores_callers_context(bool suspend, int outcome)
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        using var cancellation = new CancellationTokenSource();
        Exception? error = outcome switch
        {
            1 => new InvalidOperationException("subscriber failure"),
            2 => new OperationCanceledException(cancellation.Token),
            _ => null,
        };
        var completion = new NotificationCompletion();
        var context = new AsyncLocal<string?> { Value = "caller" };
        if (!suspend) completion.Complete(error);
        var pending = mediator.PublishAsync(new ControlledNotification(completion, completion.Version, context));
        Assert.Equal("caller", context.Value);
        if (suspend)
        {
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, completion.Consumptions);
            completion.Complete(error);
        }
        if (error is null) await pending;
        else if (error is OperationCanceledException)
        {
            var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            Assert.Equal(cancellation.Token, observed.CancellationToken);
        }
        else Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending));
        Assert.Equal(1, completion.Consumptions);
        Assert.Equal("caller", context.Value);
    }

    [Fact]
    public async Task Single_subscriber_publish_reports_synchronous_failures_through_its_result()
    {
        var services = Services();
        var factoryFailure = new InvalidOperationException("factory");
        var failFactory = false;
        services.AddTransient(_ => failFactory ? throw factoryFailure : new ControlledNotificationHandler());
        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
            var rejected = mediator.PublishAsync((ControlledNotification)null!);
            Assert.True(rejected.IsFaulted);
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await rejected);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var completion = new NotificationCompletion();
            var canceled = mediator.PublishAsync(new ControlledNotification(completion, completion.Version, new AsyncLocal<string?>()), cancellation.Token);
            Assert.True(canceled.IsCanceled);
            Assert.Equal(cancellation.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await canceled)).CancellationToken);
            Assert.Equal(0, completion.Consumptions);
        }
        failFactory = true;
        await using (var scope = provider.CreateAsyncScope())
        {
            var completion = new NotificationCompletion();
            var failed = scope.ServiceProvider.GetRequiredService<IZendiator>()
                .PublishAsync(new ControlledNotification(completion, completion.Version, new AsyncLocal<string?>()));
            Assert.True(failed.IsFaulted);
            Assert.Same(factoryFailure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await failed));
        }
    }

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
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        scope.ServiceProvider.GetRequiredService<AuditLog>().Events.Clear();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_leading_subscriber_failure_or_cancellation_keeps_later_dependencies_unresolved(bool cancel)
    {
        var services = Services();
        var constructions = 0;
        services.AddScoped<SharedLeadingLaterA>(_ =>
        {
            constructions++;
            throw new InvalidOperationException("must not construct later A");
        });
        services.AddScoped<SharedLeadingLaterB>(_ =>
        {
            constructions++;
            throw new InvalidOperationException("must not construct later B");
        });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var completion = new NotificationCompletion();
        var failure = new InvalidOperationException("shared subscriber failure");
        var pending = scope.ServiceProvider.GetRequiredService<IZendiator>()
            .PublishAsync(new SharedLeadingNote(completion, completion.Version), cancellation.Token);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, constructions);
        if (cancel) cancellation.Cancel();
        completion.Complete(cancel ? null : failure);
        if (cancel)
        {
            var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            Assert.Equal(cancellation.Token, observed.CancellationToken);
        }
        else Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(async () => await pending));
        Assert.Equal(0, constructions);
        Assert.Equal(1, completion.Consumptions);
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
