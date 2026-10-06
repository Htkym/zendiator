using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed record FaultedCancellationNotification : INotification;

public sealed class FaultedCancellationNotificationHandler : INotificationHandler<FaultedCancellationNotification>
{
    public ValueTask HandleAsync(FaultedCancellationNotification notification, CancellationToken cancellationToken)
        => new(Task.FromException(new OperationCanceledException(cancellationToken)));
}

public sealed class NotificationCancellationContractTests
{
    [Fact]
    public async Task Single_subscriber_faulted_task_with_cancellation_exception_reports_canceled_publish()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();

        var pending = scope.ServiceProvider.GetRequiredService<IZendiator>()
            .PublishAsync(new FaultedCancellationNotification(), cancellation.Token);

        Assert.True(pending.IsCanceled);
        Assert.False(pending.IsFaulted);
        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(cancellation.Token, observed.CancellationToken);
    }
}

public sealed record CompletedContextNotification(ContextMutatingSource Source) : INotification;

public sealed class CompletedContextNotificationHandler : INotificationHandler<CompletedContextNotification>
{
    public ValueTask HandleAsync(CompletedContextNotification notification, CancellationToken cancellationToken)
        => new(notification.Source, 0);
}

public sealed class ContextMutatingSource(AsyncLocal<string?> context) : System.Threading.Tasks.Sources.IValueTaskSource
{
    public int Consumptions { get; private set; }

    public void GetResult(short token)
    {
        Consumptions++;
        context.Value = "source";
    }

    public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)
        => System.Threading.Tasks.Sources.ValueTaskSourceStatus.Succeeded;

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)
        => throw new InvalidOperationException("Completed source must not register a continuation.");
}

public sealed class NotificationContextContractTests
{
    [Fact]
    public async Task Completed_source_does_not_mutate_publish_callers_context()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = new AsyncLocal<string?> { Value = "caller" };
        var source = new ContextMutatingSource(context);

        var pending = scope.ServiceProvider.GetRequiredService<IZendiator>()
            .PublishAsync(new CompletedContextNotification(source));

        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal("caller", context.Value);
        await pending;
        Assert.Equal(1, source.Consumptions);
        Assert.Equal("caller", context.Value);
    }
}

public sealed record SynchronousSuccessNotification : INotification;

public sealed class SynchronousSuccessNotificationHandler : INotificationHandler<SynchronousSuccessNotification>
{
    public ValueTask HandleAsync(SynchronousSuccessNotification notification, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}

public sealed class NotificationFastPathAllocationTests
{
    [Fact]
    public async Task Single_subscriber_completed_success_has_zero_steady_state_allocation()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var notification = new SynchronousSuccessNotification();
        for (var i = 0; i < 1024; i++) await mediator.PublishAsync(notification);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8192; i++) await mediator.PublishAsync(notification);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
