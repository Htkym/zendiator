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

public sealed class ContextMutatingSource : System.Threading.Tasks.Sources.IValueTaskSource
{
    private readonly AsyncLocal<string?> context;
    private readonly bool suspend;
    private System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore<bool> completion = new() { RunContinuationsAsynchronously = true };

    public ContextMutatingSource(AsyncLocal<string?> context, bool suspend = false)
    {
        this.context = context;
        this.suspend = suspend;
        if (!suspend) Complete();
    }

    public int Consumptions { get; private set; }
    public void Complete() => completion.SetResult(true);

    public void GetResult(short token)
    {
        Consumptions++;
        completion.GetResult(token);
        context.Value = "source";
    }

    public System.Threading.Tasks.Sources.ValueTaskSourceStatus GetStatus(short token)
        => completion.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token,
        System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags flags)
    {
        if (!suspend) throw new InvalidOperationException("Completed source must not register a continuation.");
        completion.OnCompleted(continuation, state, token, flags);
    }
}

public sealed class NotificationContextContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_completion_does_not_mutate_publish_callers_context(bool suspend)
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = new AsyncLocal<string?> { Value = "caller" };
        var source = new ContextMutatingSource(context, suspend);

        var pending = scope.ServiceProvider.GetRequiredService<IZendiator>()
            .PublishAsync(new CompletedContextNotification(source));

        Assert.Equal("caller", context.Value);
        if (suspend)
        {
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, source.Consumptions);
            source.Complete();
        }
        else Assert.True(pending.IsCompletedSuccessfully);
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
