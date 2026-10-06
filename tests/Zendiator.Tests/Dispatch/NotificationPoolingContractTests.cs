using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Zendiator.Tests;

public sealed record TaskBackedNotification(TaskCompletionSource Completion, AsyncLocal<string?> Context) : INotification;

public sealed class TaskBackedNotificationHandler : INotificationHandler<TaskBackedNotification>
{
    public ValueTask HandleAsync(TaskBackedNotification notification, CancellationToken cancellationToken)
    {
        notification.Context.Value = "subscriber";
        return new(notification.Completion.Task);
    }
}

public sealed class NotificationPoolingContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Delayed_task_completion_preserves_publish_status_and_context(int outcome)
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var context = new AsyncLocal<string?> { Value = "caller" };
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = scope.ServiceProvider.GetRequiredService<IZendiator>()
            .PublishAsync(new TaskBackedNotification(completion, context), cancellation.Token);

        Assert.False(pending.IsCompleted);
        Assert.Equal("caller", context.Value);
        // Consume this ValueTask once; inspect the preserved Task after completion.
        var published = pending.AsTask();
        if (outcome == 0)
        {
            completion.SetResult();
            await published.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(published.IsCompletedSuccessfully);
        }
        else if (outcome == 1)
        {
            var failure = new InvalidOperationException("subscriber failure");
            completion.SetException(failure);
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
                () => published.WaitAsync(TimeSpan.FromSeconds(10))));
            Assert.True(published.IsFaulted);
        }
        else
        {
            completion.SetException(new OperationCanceledException(cancellation.Token));
            Assert.True(completion.Task.IsFaulted);
            var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => published.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(cancellation.Token, observed.CancellationToken);
            Assert.True(published.IsCanceled);
            Assert.False(published.IsFaulted);
        }
        Assert.Equal("caller", context.Value);
    }

    [Fact]
    public async Task Repeated_overlapping_publishes_keep_outcomes_and_source_consumption_independent()
    {
        await using var provider = Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        using var cancellation = new CancellationTokenSource();
        var context = new AsyncLocal<string?> { Value = "caller" };

        for (var round = 0; round < 2; round++)
        {
            var sources = new NotificationCompletion[3];
            var tasks = new Task[3];
            var failures = new Exception?[3];
            for (var i = 0; i < sources.Length; i++)
            {
                sources[i] = new NotificationCompletion();
                failures[i] = ((i + round) % 3) switch
                {
                    1 => new InvalidOperationException($"failure {round}:{i}"),
                    2 => new OperationCanceledException(cancellation.Token),
                    _ => null,
                };
                tasks[i] = mediator.PublishAsync(new ControlledNotification(sources[i], sources[i].Version, context)).AsTask();
            }
            Assert.Equal("caller", context.Value);
            for (var i = sources.Length - 1; i >= 0; i--) sources[i].Complete(failures[i]);
            for (var i = 0; i < tasks.Length; i++)
            {
                if (failures[i] is OperationCanceledException)
                {
                    var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                        () => tasks[i].WaitAsync(TimeSpan.FromSeconds(10)));
                    Assert.Equal(cancellation.Token, observed.CancellationToken);
                    Assert.True(tasks[i].IsCanceled);
                }
                else if (failures[i] is InvalidOperationException failure)
                {
                    Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(
                        () => tasks[i].WaitAsync(TimeSpan.FromSeconds(10))));
                    Assert.True(tasks[i].IsFaulted);
                }
                else await tasks[i].WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(1, sources[i].Consumptions);
            }
            Assert.Equal("caller", context.Value);
        }
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddZendiator();
        return services;
    }
}
